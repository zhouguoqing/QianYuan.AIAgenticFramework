# QianYuan · 乾元 Agentic Framework

一个用 C# .NET 10 写的 Agentic 框架，参考 ReAct 范式，支持渐进式技能 (Skill) 加载、
多家大模型 Provider、MCP Server、图像识别、流式 WebAPI、React WebUI、钉钉集成。

## 特性

| 维度 | 实现 |
|------|------|
| 语言/平台 | C# 13 / .NET 10 |
| Agent 模式 | ReAct (Thought-Action-Observation) 循环；Loop Engineering；Agent-as-Tool 嵌套调用 |
| Skill 体系 | 抽象 `ISkill` + `SkillManager` 渐进式加载；按用户意图打分挑选 topK |
| Markdown Skill | 从指定目录递归加载业界常见 `Skill.md` / `SKILL.md`，将 frontmatter 映射为 Skill 清单，正文作为激活后的系统提示 |
| 模型 Provider | OpenAI 兼容 (GPT/Kimi/MiniMax/Qwen-compat/DeepSeek/OpenRouter/NEWAPI)、Azure OpenAI、Anthropic Claude、Google Gemini、Qwen DashScope 原生 |
| 多模态 | 文本 + 图像 (URL / base64) + 工具调用 |
| 流式输出 | SSE (`/api/chat/stream`) + SignalR Hub (`/hubs/chat`) |
| Web 搜索 | DuckDuckGo (免 Key) / Tavily / Bing / Brave |
| Vision 技能 | `image_describe` 工具，路由到任意支持视觉的 Provider |
| MCP | JSON-RPC 2.0 Client (stdio) + Server (HTTP/SSE + 把本地 Skill 暴露给外部) |
| Agent 注册 | `IAgentRegistry`，Agent 之间可互相调用 (`agent.<id>` 工具) |
| Agent Store | 可视化创建、编辑、编排和测试企业智能体；支持挂载 Skill、MCP Server、CLI Service |
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
│   ├── QianYuan.UnifyCli/                # 统一 HTTPS 服务封装框架 (REST API → CLI → Skill)
│   ├── QianYuan.Integrations.DingTalk/   # 钉钉 webhook 收发
│   ├── QianYuan.Api/                     # ASP.NET Core 10 host (SSE + SignalR + Swagger + Agent Store API)
│   └── QianYuan.Web/                     # React + Vite WebUI（含 Agent Store 管理界面）
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

### 4.1 启动 WorkPartner 桌面壳

```bash
cd src/QianYuan.Desktop
npm install
npm run dev
```

Electron 主进程会启动本地 `QianYuan.Api`，并打开现有 WebUI。开发模式默认读取
`http://127.0.0.1:5173`，详细说明见 [docs/WORKPARTNER_DESKTOP.md](docs/WORKPARTNER_DESKTOP.md)。

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
2. 通过 `LoopEngineeringRuntime` 注入稳定 harness、循环状态，并在上下文过大时压缩旧消息。
3. 把已挑选 Skill 的工具 + 注册的其他 Agent (作为 `agent.<id>` 工具) 合并发给 LLM。
4. 流式接收 LLM 输出：
   - 文本/思考 → 直接转发给上层。
   - ToolCall (流式 args) → 累积后通过 `IToolDispatcher` 路由到对应 Skill 或子 Agent。
   - Tool 结果 → 作为 `ChatRole.Tool` 消息追加到历史，继续下一轮。
5. 没有新 ToolCall → 终止，发 `End`。

每轮都会重新计算活动 Skill 集合，所以"渐进式扩展"是自动发生的。
默认 Agent 的最大 ReAct 迭代次数由 `QianYuan.DefaultAgentMaxIterations` 控制，默认值为 `100`；单次请求仍可通过 `MaxIterations` 覆盖。

### Loop Engineering

`QianYuan.Kernel.ReAct.LoopEngineeringOptions` 参考 Claude Code 的 agent loop/harness 思路，把 ReAct 从“简单 while tool-call”升级为可控后端循环：

- **Harness Prompt**：默认提示模型按 inspect → plan → act → observe → verify 工作；每次工具调用前明确收益，每次 observation 后基于证据更新策略。
- **Prompt Injection Boundary**：明确把工具输出、网页、文件、MCP 返回值等外部内容视为数据，而不是新的系统/开发者指令。
- **Loop State**：每轮 system prompt 注入当前迭代、最大迭代、总工具调用数和各工具使用次数，让模型知道自己处于第几轮、还剩多少预算。
- **Context Compression**：当 transcript 超过 `MaxTranscriptCharacters` 时，把旧消息压缩成连续性摘要，保留最近 `MinRecentMessagesToKeep` 条消息，降低长任务上下文爆炸风险。
- **Tool Guards**：支持 `MaxToolCalls` 总预算和重复相同工具调用拦截，避免模型盲目重试、死循环或浪费 token。
- **Observation Bounds**：限制工具结果回填长度，避免单次 observation 挤爆上下文；UI 仍能看到截断后的可读摘要。
- **Agent-level Override**：`ReActAgentDefinition.LoopEngineering` 可为不同 Agent 设置不同循环策略，比如研究型 Agent 放宽预算、执行型 Agent 收紧重复调用阈值。

完整循环链路：

```text
User messages
  └─► SelectRelevantAsync(intent) 渐进式选 Skill
       └─► LoopEngineeringRuntime.PrepareMessages
            ├─ 合并业务 SystemPrompt
            ├─ 注入 Loop harness 与循环状态
            ├─ 注入 active skill instructions
            └─ 必要时压缩旧上下文
                 └─► ILlmProvider.StreamAsync
                      ├─ TextDelta / ThinkingDelta → 透传给上层
                      └─ ToolCall → duplicate/budget guard
                           └─► IToolDispatcher.InvokeAsync
                                └─► bounded observation → ChatRole.Tool → 下一轮
```

默认已启用；可在构造 `ReActEngineOptions` 时覆盖：

```csharp
new ReActEngineOptions
{
    MaxIterations = 100,
    LoopEngineering = new LoopEngineeringOptions
    {
        MaxTranscriptCharacters = 80_000,
        MinRecentMessagesToKeep = 12,
        MaxObservationCharacters = 12_000,
        MaxToolCalls = 40,
        MaxConsecutiveIdenticalToolCalls = 1,
        HarnessPrompt = "your custom loop harness"
    }
}
```

也可以在 Agent 定义层覆盖：

```csharp
new ReActAgentDefinition
{
    Id = "researcher",
    Name = "Research Agent",
    Description = "Long-running research agent",
    LoopEngineering = new LoopEngineeringOptions
    {
        MaxToolCalls = 80,
        MaxTranscriptCharacters = 120_000,
        MaxConsecutiveIdenticalToolCalls = 2
    }
}
```

常用参数：

| 参数 | 默认值 | 作用 |
|------|--------|------|
| `Enabled` | `true` | 总开关；关闭后退回普通 ReAct 消息构造。 |
| `AddHarnessPrompt` | `true` | 是否注入默认 loop harness。 |
| `IncludeLoopStateInPrompt` | `true` | 是否注入迭代数与工具使用计数。 |
| `MaxTranscriptCharacters` | `80_000` | 超过该字符数后压缩旧 transcript。 |
| `MinRecentMessagesToKeep` | `12` | 上下文压缩时保留的最近消息数量。 |
| `MaxObservationCharacters` | `12_000` | 单次工具 observation 回填最大长度。 |
| `MaxConsecutiveIdenticalToolCalls` | `1` | 连续相同工具名 + JSON 参数允许次数。 |
| `MaxToolCalls` | `null` | 单次 run 的工具调用总预算；`null` 表示不额外限制。 |

建议：

- **默认 Agent**：保持默认配置即可，能防止大多数重复调用和上下文膨胀。
- **研究/检索 Agent**：适当提高 `MaxToolCalls`、`MaxTranscriptCharacters`，保留更多探索空间。
- **生产执行 Agent**：设置明确的 `MaxToolCalls`，并保持 `MaxConsecutiveIdenticalToolCalls = 1`，避免不可控重复执行。
- **高风险工具**：在 Skill/Dispatcher 层继续做权限与幂等控制；Loop Engineering 是循环治理，不替代工具级安全校验。

```json
{
  "QianYuan": {
    "DefaultAgentMaxIterations": 100
  }
}
```

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

## Skill 文件体系与扩展注册

QianYuan 支持三类 Skill 来源：代码实现的 `ISkill`、目录中的 Markdown Skill、以及外部 MCP Server 暴露的工具。
所有来源最终都会进入 `ISkillManager`，以统一的 manifest 参与渐进式选择；当某个 Skill 被选中时，它的工具会进入 LLM tools，
它的 `SystemPromptFragment` 也会注入当前轮 system prompt。

Skill 能力分层：

| 类型 | 能力 | 典型用途 | 是否暴露工具 |
|------|------|----------|--------------|
| Markdown Skill | 根据 `SKILL.md` 注入领域提示 | code review、需求分析、API 设计规范 | 否 |
| 内置 Skill | Web 搜索、视觉、文件系统、脚本执行 | 联网查询、图片理解、沙箱文件读写、运行代码片段 | 是 |
| 自定义 `ISkill` | 任意业务工具或系统集成 | 调内部服务、工作流编排、专有数据查询 | 是 |
| MCP Skill | 调用外部 MCP Server 工具 | filesystem、browser、database、第三方工具生态 | 是 |

其中脚本执行由内置 `qianyuan.code` Skill 提供，MCP Server 调用由 `McpSkill` 适配为一组命名空间化工具。

### Markdown Skill 文件体系

可以把 Claude/Copilot/Cursor 等常见的 `Skill.md` / `SKILL.md` 目录挂载到 QianYuan。框架会读取 YAML frontmatter 中的
`name`、`description`、`tags`、`id` 等字段，注册成渐进式 Skill；当该 Skill 被选中时，Markdown 正文会注入系统提示。

目录约定：

- 每个 Skill 一个独立目录，目录内放 `SKILL.md` 或 `Skill.md`。
- `Recursive = true` 时会递归扫描子目录，适合挂载已有的 agent skill 仓库。
- `id` 可在 frontmatter 显式声明；未声明时会用 `IdPrefix + 相对目录` 生成稳定 ID。
- 同一挂载目录内 ID 重复时，后续重复项会被跳过并记录 warning。
- Markdown Skill 是提示型 Skill，`ApproximateToolCount = 0`，不直接暴露工具调用。

支持的 frontmatter 字段：

| 字段 | 作用 | 备注 |
|------|------|------|
| `id` | Skill 唯一标识 | 可选；会规范化为小写点分 ID |
| `name` / `title` | Skill 展示名称 | 没有时回退到目录名 |
| `description` / `summary` | 用于渐进式选择的描述 | 没有时回退到正文第一行 |
| `tags` / `keywords` / `categories` | 检索标签 | 支持 `[a, b]` 或 YAML list |

示例文件结构：

```text
skills/
  code-review/
    SKILL.md
  pdf/
    Skill.md
```

示例 `SKILL.md`：

```markdown
---
id: sample.code-review
name: code-review
description: Review code for bugs, regressions, and missing tests
tags: [review, testing]
---

# Code Review

Prioritize correctness issues before style comments.
```

### 动态加载目录

在 `QianYuan.SkillDirectories` 中声明要挂载的 Skill 目录：

```json
{
  "QianYuan": {
    "SkillDirectories": [
      {
        "Path": "./.agents/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "agent"
      },
      {
        "Path": "./samples/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "sample"
      },
      {
        "Path": "/Users/you/.agents/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "agent"
      }
    ]
  }
}
```

仓库内置了一个从 [skills.sh](https://skills.sh/) 下载的项目级 Skill 目录，以及一个可直接动态加载的示例目录：

```text
.agents/skills/
  brainstorming/SKILL.md
  brainstorm/SKILL.md
  find-skills/SKILL.md
  pdf/SKILL.md
  skill-creator/SKILL.md
  summarize/SKILL.md
  using-superpowers/SKILL.md
```

这些技能通过官方 Skills CLI 安装，并由 `skills-lock.json` 记录来源与内容哈希；可用以下命令复原或更新：

```bash
npx skills experimental_install
npx skills update -p -y
```

当前内置下载来源包括：

| Skill | 来源 | 说明 |
|-------|------|------|
| `using-superpowers` / `brainstorming` | `archieindian/openclaw-superpowers` | Superpowers 工作流与前置构思流程 |
| `brainstorm` | `buiducnhat/agent-skills` | 轻量构思与方案收敛流程 |
| `find-skills` | `vercel-labs/skills` | 从 skills.sh 发现与安装技能 |
| `skill-creator` / `pdf` | `anthropics/skills` | 创建/优化 Skill，以及 PDF 读取与处理 |
| `summarize` | `sjunepark/custom-skills` | URL、本地文件、媒体等内容摘要 |

示例目录：

```text
samples/skills/
  api-design/SKILL.md
  code-review/SKILL.md
  debugging/SKILL.md
  docs-writing/SKILL.md
  requirements-analysis/SKILL.md
```

默认 `appsettings.json` 已启用 `./.agents/skills` 和 `./samples/skills`。启动 API 后可通过 `GET /api/skills` 查看这些 Skill 是否注册成功。

API 启动时会执行：

```csharp
app.Services.RegisterMarkdownSkillsFromDirectories(qy.SkillDirectories.Select(d => new MarkdownSkillDirectoryOptions
{
  Path = d.Path,
  Recursive = d.Recursive,
  Enabled = d.Enabled,
  IdPrefix = d.IdPrefix,
}));
```

### 代码 Skill 注册

需要真实工具能力时，实现 `ISkill`，提供 manifest 属性、工具定义和调用逻辑：

```csharp
public sealed class MySkill : ISkill
{
  public string Id => "my.skill";
  public string Name => "My Skill";
  public string Description => "Does one focused job.";
  public IReadOnlyList<string> Tags => ["custom"];
  public string? SystemPromptFragment => "Use this skill only when the task matches its description.";

  public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
    => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>([
      new ToolDefinition(
        "my_tool",
        "Run my custom operation.",
        "{\"type\":\"object\",\"properties\":{}}")
    ]);

  public ValueTask<SkillInvocationResult> InvokeAsync(
    string toolName,
    string argumentsJson,
    SkillInvocationContext context,
    CancellationToken ct = default)
    => ValueTask.FromResult(SkillInvocationResult.Ok("{\"ok\":true}"));
}
```

注册方式有两种。

通过 DI 注册，适合普通应用启动：

```csharp
builder.Services.AddSingleton<ISkill, MySkill>();
// app.Build() 后统一挂载到 SkillManager
app.Services.RegisterSkillsFromServices();
```

直接注册到 `ISkillManager`，适合运行期管理、插件发现或测试：

```csharp
var manager = app.Services.GetRequiredService<ISkillManager>();
manager.Register(new MySkill());
```

如果 Skill 初始化成本较高，也可以只注册轻量 manifest + factory，首次命中时再物化：

```csharp
manager.Register(
  new SkillManifest(
    "my.lazy-skill",
    "Lazy Skill",
    "Loads resources only when selected.",
    ["custom", "lazy"],
    ApproximateToolCount: 1,
    RequiresNetwork: false,
    RequiresFilesystem: false),
  sp => new MySkill());
```

### 脚本执行 Skill

内置 Code Execution Skill 会暴露 `code_run` 工具，用于在沙箱目录中执行短脚本。默认关闭，需要显式启用：

```json
{
  "QianYuan": {
    "CodeExecution": {
      "Enabled": true,
      "SandboxDirectory": "./_sandbox/code",
      "AllowedRuntimes": ["python", "node"],
      "TimeoutSeconds": 20
    }
  }
}
```

启用后启动流程会注册：

```csharp
builder.Services.AddCodeExecutionSkill(new CodeExecutionOptions
{
    SandboxDirectory = cx.SandboxDirectory,
    AllowedRuntimes = new HashSet<string>(cx.AllowedRuntimes, StringComparer.OrdinalIgnoreCase),
    PerCallTimeout = TimeSpan.FromSeconds(cx.TimeoutSeconds),
});
```

工具协议：

```json
{
  "runtime": "python",
  "code": "print(1 + 1)"
}
```

当前支持的运行时由 `AllowedRuntimes` 控制，内置实现支持 `python`、`node`、`bash`；建议生产环境只开放必要运行时，并把 `SandboxDirectory` 指向隔离目录。

### MCP Skill 注册

外部 MCP Server 可以作为 Skill 挂载。配置 stdio server 后，启动时调用 `MountMcpSkills()`，每个 MCP client 会被适配成一个 `McpSkill`：

```json
{
  "QianYuan": {
    "McpServers": [
      {
        "ServerId": "fs",
        "Command": "npx",
        "Arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
        "Environment": {}
      }
    ]
  }
}
```

```csharp
builder.Services.AddMcpStdioServer(new McpStdioServerConfig
{
  ServerId = "fs",
  Command = "npx",
  Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
});

app.Services.MountMcpSkills();
```

挂载后 Skill ID 为 `mcp.<serverId>`，工具名会统一前缀化为 `mcp.<serverId>.<toolName>`，避免多个 MCP Server 之间的工具名冲突。工具列表会延迟到第一次需要该 Skill 时通过 MCP `ListTools` 获取，调用时再转发到对应 MCP Server 的 `CallTool`。

### 注册生命周期

启动流程里，Skill 注册顺序是：

1. `RegisterSkillsFromServices()`：挂载内置 Skill 和通过 DI 注册的自定义 `ISkill`。
2. `RegisterMarkdownSkillsFromDirectories(...)`：按配置目录动态加载 `SKILL.md` / `Skill.md`。
3. `MountMcpSkills()`：把外部 MCP Server 工具挂载为 Skill。

注册完成后，`GET /api/skills` 可查看 catalog；ReAct 每轮会调用 `SelectRelevantAsync(intent, topK)` 选择当前最相关的 Skill。

这类 Markdown Skill 是“提示型技能”，不会执行外部命令或暴露工具调用；需要真实工具能力时仍建议实现 `ISkill` 或通过 MCP 挂载。
## Agent Store：企业智能体商店

Agent Store 是 QianYuan 面向企业 Agent 落地的可视化编排与运行入口，用来把“可复用能力”沉淀成可管理、可测试、可上线的 Agent。
它不是单纯的提示词列表，而是把 Agent Profile、模型 Provider、系统提示、Skill、MCP Server 与 CLI Service 组合成一个完整智能体。

### 核心能力

- **Agent 档案管理**：创建、编辑、删除 Agent，维护 `id`、名称、描述、默认 Provider、默认模型与 system prompt。
- **Skill 编排**：从 `ISkillManager` 已注册的 Markdown Skill、内置 Skill、自定义 `ISkill` 中选择能力，并按优先级挂载到指定 Agent。
- **MCP Server 集成**：为单个 Agent 关联外部 MCP Server，把文件系统、浏览器、数据库、第三方工具等 MCP 能力纳入工具列表。
- **CLI Service 集成**：通过 UnifyCli 把内部微服务或第三方 HTTPS API 封装为 Agent 工具，支持认证、参数映射和响应转换。
- **工具清单与单工具测试**：查看当前 Agent 聚合后的全部工具，并可直接传入 JSON 参数测试单个工具。
- **Agent 交互测试**：在 WebUI 中直接向指定 Agent 发起对话，验证模型、提示词和工具链协同效果。

### WebUI 使用方式

启动 WebUI 后，在界面进入 **Agent Store**：

1. 点击“新建 Agent”，填写唯一 ID、名称、描述、Provider、模型和系统提示。
2. 在 **Skills** 页签挂载已有 Skill，并设置 priority 控制优先级。
3. 在 **MCP** 页签添加 MCP Server，例如 filesystem、browser、database 等工具服务。
4. 在 **CLI** 页签添加通过 UnifyCli 暴露的 HTTPS 服务。
5. 在 **Test** 页签查看工具列表、测试单个工具，或直接与 Agent 对话。

### Agent Store API

后端统一暴露在 `/api/agent-store`：

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/agent-store` | 获取 Agent 列表 |
| `GET` | `/api/agent-store/{agentId}` | 获取指定 Agent 详情 |
| `POST` | `/api/agent-store` | 创建 Agent |
| `PUT` | `/api/agent-store/{agentId}` | 更新 Agent |
| `DELETE` | `/api/agent-store/{agentId}` | 删除 Agent |
| `POST` | `/api/agent-store/{agentId}/skills` | 给 Agent 挂载 Skill |
| `DELETE` | `/api/agent-store/{agentId}/skills/{skillRowId}` | 移除已挂载 Skill |
| `POST` | `/api/agent-store/{agentId}/mcp-servers` | 关联 MCP Server |
| `DELETE` | `/api/agent-store/{agentId}/mcp-servers/{serverRowId}` | 移除 MCP Server |
| `POST` | `/api/agent-store/{agentId}/cli-services` | 关联 CLI Service |
| `DELETE` | `/api/agent-store/{agentId}/cli-services/{serviceRowId}` | 移除 CLI Service |
| `GET` | `/api/agent-store/{agentId}/tools` | 获取该 Agent 聚合后的工具清单 |
| `POST` | `/api/agent-store/{agentId}/test-tool` | 测试单个工具调用 |
| `POST` | `/api/agent-store/{agentId}/interact` | 与该 Agent 进行交互测试 |

创建 Agent 的最小请求示例：

```json
{
  "id": "sales-assistant",
  "name": "销售助手",
  "description": "面向售前方案、客户问答和商机跟进的企业智能体",
  "defaultProviderId": "openai",
  "defaultModel": "gpt-4o-mini",
  "systemPrompt": "你是专业、稳健的企业销售助手。"
}
```

### 设计定位

Agent Store 适合承载“企业内部智能体市场”：研发、售前、运维、客服、财务等角色可以沉淀为独立 Agent；
每个 Agent 通过 Skill/MCP/CLI 组合自己的工具边界，既方便复用，也便于后续做权限、审计、发布状态、版本管理和多租户隔离。

## UnifyCli：统一 HTTPS 服务封装

### 概览

`QianYuan.UnifyCli` 是一个通用框架，用于将任何 HTTPS 服务或 REST API 统一封装为 CLI 方法，
并通过 Skill 系统无缝集成到 Agent 中。它解决了"如何让 Agent 快速调用第三方 API"的问题。

**核心场景**：
- 集成 GitHub / Slack / OpenAI 等第三方 API
- 包装内部微服务供 Agent 调用
- 数据聚合（多个 API → 统一接口）
- 实现通用的 API 网关和代理

### 架构

```
Agent Request
    ↓
CliServiceSkill (Skill Adapter)
    ↓
CliService (Method Registry)
    ↓
UnifyHttpClient (HTTP Executor)
    ├─ Parameter Interpolation (path, query, body)
    ├─ Authentication (Basic/Bearer/ApiKey/Custom)
    ├─ Retry & Timeout
    └─ Response Transformation
    ↓
External HTTPS Service / REST API
```

### 快速开始

#### 1. DI 配置

```csharp
builder.Services.AddUnifyCli();
```

#### 2. 定义 CLI 服务

```csharp
using QianYuan.UnifyCli.Implementation;
using System.Text.Json;

var userService = new CliServiceDefinition
{
    Id = "user.api",
    Name = "User Service",
    Description = "API for user management",
    BaseUri = "https://api.example.com"
};
```

#### 3. 定义 CLI 方法

```csharp
var getUserMethod = new CliMethodDefinition
{
    Id = "get_user",
    Name = "Get User",
    Description = "Get user info by ID",
    HttpMethod = "GET",
    PathTemplate = "/v1/users/{userId}",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            userId = new { type = "string", description = "User ID" }
        },
        required = new[] { "userId" }
    }),
    Tags = new[] { "user", "profile" }
};

userService.RegisterMethod(getUserMethod);
```

#### 4. 注册服务

```csharp
builder.Services.AddCliService(userService);
```

#### 5. 在 Agent 中使用

```csharp
// 自动作为 Skill 暴露给 Agent
var skillFactory = sp.GetRequiredService<CliServiceSkillFactory>();
var skill = await skillFactory.CreateSkillAsync("user.api");

// 现在 Agent 可以调用 "get_user" 工具
```

### 认证支持

UnifyCli 开箱支持 5 种认证方式：

| 方式 | 用途 | 配置 |
|------|------|------|
| **No Auth** | 公开 API | `type: "none"` |
| **Basic** | 用户名/密码 | `type: "basic"`, `username`, `password` |
| **Bearer** | JWT / OAuth2 Token | `type: "bearer"`, `token` |
| **API Key** | Header 或 Query 参数 | `type: "api_key"`, `token`, `headerName` 或 `queryParamName` |
| **Custom** | 自定义 Header | `type: "custom"`, `headers: {...}` |

示例：

```csharp
var authFactory = new AuthenticationProviderFactory();
var authOptions = new AuthenticationOptions
{
    Type = "bearer",
    Token = "eyJhbGciOiJIUzI1NiIs..."
};

service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

### 参数处理

UnifyCli 自动处理三种参数映射：

#### 路径参数

```csharp
PathTemplate = "/v1/users/{userId}/posts/{postId}"
// 调用时：InvokeAsync("...", @"{""userId"":""123"",""postId"":""456""}")
// 转换为：GET /v1/users/123/posts/456
```

#### Query 参数

```csharp
QueryParams = new Dictionary<string, string>
{
    { "limit", "$limit" },      // 从参数中取 limit
    { "sort", "date" }          // 固定值
}
// 调用时：InvokeAsync("...", @"{""limit"":10}")
// 转换为：GET /v1/items?limit=10&sort=date
```

#### Request Body

```csharp
RequestBodyTemplate = "$."  // 整个参数作为 JSON body
// 或
RequestBodyTemplate = "$.user"  // 只取参数中的 user 字段
```

### 响应转换

如果外部 API 返回复杂结构，可以用 JsonPath 提取特定字段：

```csharp
ResponseTransformer = new JsonPathResponseTransformer("$.data.items")
// 原始响应：{ "data": { "items": [...] }, "meta": {...} }
// 转换后：[...]
```

### 完整示例

详见 [samples/QianYuan.Sample.Console/UnifyCliIntegrationExample.cs](samples/QianYuan.Sample.Console/UnifyCliIntegrationExample.cs)，
包含 7 个场景：基本使用、认证、注册表发现、DI 集成、Skill 集成、错误处理、完整应用配置。

也可查看内置示例服务：

- `WeatherServiceExample`：OpenWeatherMap API (GET + 认证)
- `GitHubServiceExample`：GitHub REST API (多个 endpoint + Bearer auth)
- `SlackServiceExample`：Slack API (POST + JSON body)

### 详细文档

| 文档 | 说明 |
|------|------|
| [README.md](src/QianYuan.UnifyCli/README.md) | 完整 API 参考、配置选项、最佳实践、安全考虑 |
| [QUICKSTART.md](src/QianYuan.UnifyCli/QUICKSTART.md) | 5 分钟快速开始、常见模式、FAQ |
| [ARCHITECTURE.md](src/QianYuan.UnifyCli/ARCHITECTURE.md) | 系统架构、数据流、扩展设计 |
## 钉钉

1. 创建自定义机器人，拿 outgoing webhook URL + 加签 secret。
2. 在 `appsettings.json` 配 `QianYuan.DingTalk.Enabled = true` 等字段。
3. 把回调地址 `https://<your-host>/api/dingtalk/webhook` 配进钉钉机器人。
4. 框架会签名校验、丢给默认 Agent、把 streaming 文本周期性 markdown 推回。

## License

本项目以 [Apache License 2.0](./LICENSE) 协议开源。Copyright © 2026 QianYuan Team.
