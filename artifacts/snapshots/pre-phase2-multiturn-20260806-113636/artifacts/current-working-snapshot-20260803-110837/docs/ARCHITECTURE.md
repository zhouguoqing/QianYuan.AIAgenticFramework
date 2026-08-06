# QianYuan 架构概览

```
┌────────────────────────────────────────────────────────────────────────────┐
│                              终端 / 客户端                                  │
│   React WebUI (Vite)   钉钉机器人   外部 MCP Client (Cursor/Claude Desktop) │
└─────────────▲──────────────────▲──────────────────────▲───────────────────┘
              │ SSE/SignalR      │ Webhook              │ HTTP JSON-RPC
┌─────────────┴──────────────────┴──────────────────────┴───────────────────┐
│                          QianYuan.Api  (ASP.NET Core 10)                   │
│   ChatController · SkillsController · AgentsController · McpServerController│
│   DingTalkController · ChatHub (SignalR) · Swagger · CORS                  │
└─────────────▲──────────────────▲──────────────────────▲───────────────────┘
              │                  │                      │
              ▼                  ▼                      ▼
       ┌───────────────────────────────────────────────────────┐
       │            QianYuan.Kernel  (ReAct + 渐进式)          │
       │  ReActEngine                                          │
       │  ├─ build active skill set (SelectRelevantAsync)      │
       │  ├─ apply LoopEngineering harness/context/tool guards │
       │  ├─ stream LLM turn (provider-agnostic chunks)        │
       │  ├─ dispatch tool calls → IToolDispatcher             │
       │  └─ append observations, loop                         │
       │  SkillManager · AgentRegistry · LlmProviderRegistry   │
       │  InMemorySessionStore                                 │
       └─────▲────────────────▲─────────────────▲──────────────┘
             │                │                 │
   ┌─────────┴────────┐  ┌────┴───────────┐  ┌──┴──────────────────┐
   │   LLM Providers  │  │  Skills        │  │  MCP Client          │
   │  OpenAICompat ◀──┤  │  WebSearch     │  │  StdioMcpClient      │
   │  Anthropic    ◀──┤  │  Vision        │──▶ external MCP server  │
   │  Gemini       ◀──┤  │  FileSystem    │                          │
   │  QwenNative   ◀──┘  │  CodeExecution │                          │
   └──────────────────┘  │  (external via McpSkill)                  │
                         └────────────────────────────────────────────┘
                                            │
                              QianYuan.Core (abstractions)
```

## 关键数据流：一次带工具调用的对话

```
User → POST /api/chat/stream
       └─► ChatController
            └─► ReActAgent.RunAsync
                 └─► ReActEngine.RunAsync
                      ├─[iter 1] SkillManager.SelectRelevantAsync("北京今天天气?")
                      │           → 选中 web_search
                      │   build ChatRequest + tools
                      │   ILlmProvider.StreamAsync
                      │           → ToolCallStart{name=web_search, args}
                      │           → ToolCallEnd
                      │   ToolDispatcher.InvokeAsync("web_search", {...})
                      │           → WebSearchSkill → TavilySearchProvider
                      │   append ToolResult to history; yield Observation
                      ├─[iter 2] tools 已注入到 system prompt
                      │   ILlmProvider.StreamAsync
                      │           → TextDelta TextDelta ... End
                      └─► End
       ◀── SSE: event: text data: "今天..."
       ◀── SSE: event: tool_call_start ...
       ◀── SSE: event: tool_observation ...
       ◀── SSE: event: done
```

## 渐进式加载的两层

1. **目录层**：`SkillManager.Register(manifest, factory)` 只保留清单 (id/name/desc/tags)，
   构造延迟到 `GetAsync` 被实际调用。这避免启动时连接所有 MCP server、加载所有 embedding、
   实例化所有 HTTP client。

2. **每轮层**：`ReActEngine` 每个 ReAct 迭代用 `SelectRelevantAsync(intent)` 重新打分挑选
   topK 个 Skill。模型只能看到当前轮相关的工具列表，不会被噪声淹没。

两层之上还可叠加：MCP Skill 自身在 `GetToolsAsync` 时才真正与远端 server 握手 (`ConnectAsync`)。

## Loop Engineering

后端 ReAct 循环现在内置 `LoopEngineeringRuntime`，目标是把 Claude Code 一类 agent loop 的工程化约束沉到框架层，而不是依赖每个业务 Agent 自己写提示词。

一次迭代的顺序变为：

```
working messages
  └─► progressive skill selection
       └─► LoopEngineeringRuntime.PrepareMessages
            ├─ stable loop harness prompt
            ├─ current iteration/tool-use state
            ├─ active skill instructions
            └─ compressed historical transcript when needed
                 └─► ILlmProvider.StreamAsync
                      └─► tool call
                           ├─ duplicate/budget guard
                           ├─ IToolDispatcher.InvokeAsync
                           ├─ bounded observation
                           └─ append ChatRole.Tool
```

关键策略：

- **稳定 harness**：要求模型按 inspect → plan → act → observe → verify 循环工作，工具调用前明确收益，工具结果后基于证据调整。
- **提示注入防护**：harness 明确声明工具输出、网页、文件等外部内容只是数据，不是新的指令来源。
- **上下文压缩**：`MaxTranscriptCharacters` 控制上下文窗口，旧消息压缩成摘要，最近消息原样保留。
- **重复调用拦截**：相同工具名 + 规范化 JSON 参数连续重复超过阈值时，直接返回错误 observation，迫使模型换策略或停止。
- **工具预算**：`MaxToolCalls` 可为单次 run 设置总工具调用上限。
- **结果限长**：`MaxObservationCharacters` 限制工具 JSON 和人类摘要回填长度，避免 observation 污染后续轮次。

这些策略通过 `ReActEngineOptions.LoopEngineering` 配置，默认开启并兼容已有 Skill、Provider、Agent-as-Tool 机制。

## Provider 接入规范

每个 Provider 需要：

- 把上游 SSE / JSON-array / 自有事件协议规整成 `IAsyncEnumerable<StreamingChunk>`；
- 把 `ChatMessage`（含多模态 parts + ToolCall + ToolResult）翻译成上游消息结构；
- 把工具定义翻译成上游 schema：
  - OpenAI 兼容: `tools[].function.parameters` (JSON Schema)
  - Anthropic: `tools[].input_schema` + `cache_control` 标记
  - Gemini: `tools[0].functionDeclarations[].parameters`
  - Qwen 原生: `parameters.tools[].function.parameters`
- 把 ToolResult 角色翻译成上游期望的形态（`role:"tool"` / Claude `tool_result` block / Gemini `functionResponse`）。

## 安全与边界

- `FileSystemSkill` 所有路径相对沙箱根，越权直接抛异常。
- `CodeExecutionSkill` 默认禁用；启用时白名单 runtime、限时、限制输出长度，进程在沙箱目录里运行。
- DingTalk inbound 走 HMAC-SHA256 (timestamp+body+appSecret) 校验。
- MCP stdio 客户端启动子进程，进程组级 kill。

## 扩展点

| 想做 | 改哪里 |
|------|--------|
| 加新 LLM 厂商 | 实现 `ILlmProvider`，写一个 `Add<Vendor>Provider` 扩展 |
| 加新 Skill | 实现 `ISkill`，`services.AddSingleton<ISkill, MySkill>()` |
| 替换 Session 存储 | 实现 `ISessionStore`，DI 注册覆盖默认 InMemory |
| 自定义 Agent 循环 (Plan-and-Execute / Reflexion) | 实现 `IAgent`，不复用 `ReActAgent` |
| 把语义检索接入渐进式选择 | 实现自定义 `ISkillManager`，替换默认关键词打分 |
