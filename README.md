# QianYuan · 乾元 Agentic Framework

一个用 C# .NET 10 写的 Agentic 框架，参考 ReAct 范式，支持渐进式技能 (Skill) 加载、
多家大模型 Provider、MCP Server、图像识别、流式 WebAPI、React WebUI、钉钉集成。

## 特性

| 维度 | 实现 |
|------|------|
| 语言/平台 | C# 13 / .NET 10 |
| Agent 模式 | ReAct (Thought-Action-Observation) 循环；Agent-as-Tool 嵌套调用 |
| Skill 体系 | 抽象 `ISkill` + `SkillManager` 渐进式加载；按用户意图打分挑选 topK |
| 模型 Provider | OpenAI 兼容 (GPT/Kimi/MiniMax/Qwen-compat/DeepSeek/OpenRouter/NEWAPI)、Azure OpenAI、Anthropic Claude、Google Gemini、Qwen DashScope 原生 |
| 多模态 | 文本 + 图像 (URL / base64) + 工具调用 |
| 流式输出 | SSE (`/api/chat/stream`) + SignalR Hub (`/hubs/chat`) |
| Web 搜索 | DuckDuckGo (免 Key) / Tavily / Bing / Brave |
| Vision 技能 | `image_describe` 工具，路由到任意支持视觉的 Provider |
| MCP | JSON-RPC 2.0 Client (stdio) + Server (HTTP/SSE + 把本地 Skill 暴露给外部) |
| Agent 注册 | `IAgentRegistry`，Agent 之间可互相调用 (`agent.<id>` 工具) |
| WebUI | React 19 + Vite + TS，SSE 流式渲染、Markdown、图片粘贴 |
| 钉钉 | 自定义机器人签名校验 + 分段 Markdown 卡片更新 |

## 项目结构

```
QianYuan.AgenticFramework/
├── QianYuan.AgenticFramework.sln
├── nuget.config                # 锁定到 nuget.org
├── Directory.Build.props       # net10.0, nullable, latest C#
├── src/
│   ├── QianYuan.Core/                    # 抽象 + 模型 + 流式 chunk + 异常
│   ├── QianYuan.Kernel/                  # ReAct 引擎、SkillManager、Agent/Provider 注册表
│   ├── QianYuan.Providers.OpenAICompat/  # OpenAI 协议 (GPT/Kimi/MiniMax/Qwen-compat/NEWAPI)
│   ├── QianYuan.Providers.AzureOpenAI/   # Azure OpenAI Service (deployment + api-version)
│   ├── QianYuan.Providers.Anthropic/     # Claude Messages API
│   ├── QianYuan.Providers.Gemini/        # Gemini v1beta
│   ├── QianYuan.Providers.QwenNative/    # DashScope 原生
│   ├── QianYuan.Skills.Builtin/          # WebSearch / Vision / FileSystem / Code
│   ├── QianYuan.Mcp/                     # MCP Client (stdio) + Server core
│   ├── QianYuan.Integrations.DingTalk/   # 钉钉 webhook 收发
│   ├── QianYuan.Api/                     # ASP.NET Core 10 host (SSE + SignalR + Swagger)
│   └── QianYuan.Web/                     # React + Vite WebUI
├── samples/QianYuan.Sample.Console/
└── tests/QianYuan.Core.Tests/            # xUnit + FluentAssertions
```

## 快速开始

### 0. 一键启动脚本（推荐）

仓库内置了三平台的一键脚本，会自动 `restore` + `build` + 启动 Api 与 WebUI，
日志写到 `.runtime/logs/`，进程 PID 写到 `.runtime/*.pid`。

```bash
# macOS / Linux
./scripts/start.sh        # 启动
./scripts/start.sh --stop # 停止

# Windows (cmd / PowerShell 任一)
scripts\start.cmd
scripts\stop.cmd
# 或直接
pwsh -File scripts\start.ps1
pwsh -File scripts\start.ps1 -Stop
```

脚本会检测 `.NET 10 SDK` 与 `Node.js (>=18)`；缺 Node 时只起 Api。
默认地址：Api `http://localhost:5050`（Swagger `/swagger`），WebUI `http://localhost:5173`。
通过 `QIANYUAN_API_URL` / `QIANYUAN_WEB_URL` 环境变量可覆盖。

### 1. 编译

```bash
cd QianYuan.AgenticFramework
dotnet build
```

### 2. 配置 API Key

编辑 `src/QianYuan.Api/appsettings.json` 或使用 user-secrets / 环境变量。
任何一家 Provider 配上 ApiKey 即可启动。

#### NEWAPI / One-Hub / 第三方聚合代理

NEWAPI 完全兼容 OpenAI Chat Completions 协议，直接作为一个 `OpenAICompatProviders` 条目即可：

```json
{
  "ProviderId": "newapi",
  "BaseUrl": "https://your-newapi-host/v1",
  "ApiKey": "sk-...",
  "DefaultModel": "gpt-4o-mini",
  "SupportsVision": true
}
```

`ProviderId` 任取，Kernel 通过它路由；`BaseUrl` 是你 NEWAPI 部署的对外地址。

#### Azure OpenAI Service

Azure 的 URL 由 deployment 名决定（不是 model 名），并且需要 `api-version` 查询参数和
`api-key` 请求头。在 `AzureOpenAIProviders` 数组里配置：

```json
{
  "ProviderId": "azure-openai",
  "Endpoint": "https://your-resource.openai.azure.com",
  "ApiKey": "<your-key>",
  "DefaultDeployment": "gpt-4o",
  "ApiVersion": "2024-10-21",
  "SupportsVision": true,
  "ModelToDeployment": {
    "gpt-4o": "gpt-4o-prod",
    "gpt-4o-mini": "gpt-4o-mini-prod"
  }
}
```

- `Endpoint` 不要带 `/openai` 之类路径，框架会自动拼接。
- `ModelToDeployment` 可选，把"逻辑模型名"映射到 Azure 上的实际部署名；
  没配的话，请求里给的 `Model` 会被直接当作 deployment 用。
- 同一个数组里可以多份配置不同的 `ProviderId`，比如分别接 Sweden 与 East-US 两个资源。

### 3. 启动 WebAPI

```bash
dotnet run --project src/QianYuan.Api
# 监听 http://localhost:5050  (Swagger: /swagger)
```

### 4. 启动 WebUI

```bash
cd src/QianYuan.Web
npm install
npm run dev
# 浏览器打开 http://localhost:5173
```

Vite dev-server 已配置反向代理：`/api` 和 `/hubs` 自动转发到 5050。

### 5. 跑控制台样例

```bash
export QIANYUAN_APIKEY=sk-...
export QIANYUAN_BASEURL=https://api.openai.com/v1
export QIANYUAN_MODEL=gpt-4o-mini
dotnet run --project samples/QianYuan.Sample.Console
```

### 6. 跑单元测试

```bash
dotnet test
```

## 核心抽象（最小集）

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    string DefaultModel { get; }
    LlmCapabilities Capabilities { get; }
    Task<ChatResponse> CompleteAsync(ChatRequest req, CancellationToken ct);
    IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest req, CancellationToken ct);
}

public interface ISkill
{
    string Id { get; }
    ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct);
    ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argsJson, SkillInvocationContext ctx, CancellationToken ct);
}

public interface IAgent
{
    string Id { get; }
    IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest req, CancellationToken ct);
}
```

`StreamingChunk` 是统一的流式事件 (TextDelta / ThinkingDelta / ToolCallStart / ToolCallArgsDelta /
ToolCallEnd / ToolObservation / Usage / End / Error / Warning)，
四家 Provider 都把各自协议规整成它。

## ReAct 循环要点

`QianYuan.Kernel.ReAct.ReActEngine` 每轮：

1. 用 `ISkillManager.SelectRelevantAsync(intent, topK)` 渐进式挑选 Skill。
2. 把已挑选 Skill 的工具 + 注册的其他 Agent (作为 `agent.<id>` 工具) 合并发给 LLM。
3. 流式接收 LLM 输出：
   - 文本/思考 → 直接转发给上层。
   - ToolCall (流式 args) → 累积后通过 `IToolDispatcher` 路由到对应 Skill 或子 Agent。
   - Tool 结果 → 作为 `ChatRole.Tool` 消息追加到历史，继续下一轮。
4. 没有新 ToolCall → 终止，发 `End`。

每轮都会重新计算活动 Skill 集合，所以"渐进式扩展"是自动发生的。

## MCP

- **作为客户端**: `services.AddMcpStdioServer(new McpStdioServerConfig { ServerId="fs", Command="npx", Arguments=["-y","@modelcontextprotocol/server-filesystem","/tmp"] })`
  ，启动后 `sp.MountMcpSkills()` 把外部 MCP Server 的所有工具挂成名为 `mcp.fs` 的 Skill。
- **作为服务端**: WebAPI 暴露 `POST /api/mcp`，把本地 SkillManager 的所有工具按 MCP 协议提供给外部 MCP Client (Claude Desktop / Cursor / etc.)。

## Web 搜索

```json
"WebSearch": {
  "Provider": "duckduckgo",
  "ApiKey": ""
}
```

- `duckduckgo` / `ddg`（默认）：抓取 `html.duckduckgo.com` 的免 Key 服务，开箱即用，
  适合本地开发和轻量场景；DDG 会限速，重负载请改用付费服务。
- `tavily` / `bing` / `brave`：填入相应平台的 `ApiKey` 即可。
- 任何 Provider 的 ApiKey 为空时，框架会自动回退到 DuckDuckGo。

## 钉钉

1. 创建自定义机器人，拿 outgoing webhook URL + 加签 secret。
2. 在 `appsettings.json` 配 `QianYuan.DingTalk.Enabled = true` 等字段。
3. 把回调地址 `https://<your-host>/api/dingtalk/webhook` 配进钉钉机器人。
4. 框架会签名校验、丢给默认 Agent、把 streaming 文本周期性 markdown 推回。

## License

本项目以 [Apache License 2.0](./LICENSE) 协议开源。Copyright © 2026 QianYuan Team.
