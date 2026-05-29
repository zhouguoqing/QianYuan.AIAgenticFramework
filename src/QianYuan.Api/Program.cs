using Microsoft.AspNetCore.HttpOverrides;
using QianYuan.Api.Configuration;
using QianYuan.Api.Hubs;
using QianYuan.Core.Abstractions;
using QianYuan.Integrations.DingTalk;
using QianYuan.Kernel;
using QianYuan.Kernel.Agents;
using QianYuan.Kernel.Skills;
using QianYuan.Mcp;
using QianYuan.Mcp.Client;
using QianYuan.Providers.Anthropic;
using QianYuan.Providers.AzureOpenAI;
using QianYuan.Providers.Gemini;
using QianYuan.Providers.OpenAICompat;
using QianYuan.Providers.QwenNative;
using QianYuan.Skills.Builtin;
using QianYuan.Skills.Builtin.Code;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<QianYuanApiOptions>(builder.Configuration.GetSection("QianYuan"));
var qy = builder.Configuration.GetSection("QianYuan").Get<QianYuanApiOptions>() ?? new();

// --- core kernel ---
builder.Services.AddQianYuanKernel();

// --- LLM providers ---
foreach (var p in qy.OpenAICompatProviders)
{
    if (string.IsNullOrEmpty(p.ApiKey)) continue;
    builder.Services.AddOpenAICompatProvider(new OpenAICompatOptions
    {
        ProviderId = p.ProviderId,
        BaseUrl = p.BaseUrl,
        ApiKey = p.ApiKey,
        DefaultModel = p.DefaultModel,
        SupportsVision = p.SupportsVision,
        SupportsSamplingParams = p.SupportsSamplingParams,
    });
}
foreach (var a in qy.AzureOpenAIProviders)
{
    if (string.IsNullOrEmpty(a.ApiKey) || string.IsNullOrEmpty(a.Endpoint) || string.IsNullOrEmpty(a.DefaultDeployment))
        continue;
    builder.Services.AddAzureOpenAIProvider(new AzureOpenAIOptions
    {
        ProviderId = a.ProviderId,
        Endpoint = a.Endpoint,
        ApiKey = a.ApiKey,
        DefaultDeployment = a.DefaultDeployment,
        ApiVersion = a.ApiVersion,
        SupportsVision = a.SupportsVision,
        SupportsTools = a.SupportsTools,
        SupportsParallelToolCalls = a.SupportsParallelToolCalls,
        ModelToDeployment = a.ModelToDeployment.Count > 0 ? a.ModelToDeployment : null,
    });
}
if (qy.Anthropic is { ApiKey.Length: > 0 } ant)
{
    builder.Services.AddAnthropicProvider(new AnthropicOptions
    {
        ProviderId = ant.ProviderId,
        ApiKey = ant.ApiKey,
        DefaultModel = ant.DefaultModel,
        BaseUrl = ant.BaseUrl,
        EnableExtendedThinking = ant.EnableExtendedThinking,
        ThinkingBudgetTokens = ant.ThinkingBudgetTokens,
    });
}
if (qy.Gemini is { ApiKey.Length: > 0 } gem)
{
    builder.Services.AddGeminiProvider(new GeminiOptions
    {
        ProviderId = gem.ProviderId,
        ApiKey = gem.ApiKey,
        DefaultModel = gem.DefaultModel,
    });
}
if (qy.Qwen is { ApiKey.Length: > 0 } qw)
{
    builder.Services.AddQwenProvider(new QwenOptions
    {
        ProviderId = qw.ProviderId,
        ApiKey = qw.ApiKey,
        DefaultModel = qw.DefaultModel,
    });
}

// --- built-in skills ---
// Web search: DuckDuckGo needs no key — use it whenever the configured provider is "duckduckgo"
// (the default) or whenever any other provider is selected but its ApiKey is empty.
if (qy.WebSearch is { } ws)
{
    var providerName = (ws.Provider ?? "").ToLowerInvariant();
    var hasKey = !string.IsNullOrEmpty(ws.ApiKey);
    switch (providerName)
    {
        case "bing"   when hasKey: builder.Services.AddBingWebSearchSkill(ws.ApiKey); break;
        case "brave"  when hasKey: builder.Services.AddBraveWebSearchSkill(ws.ApiKey); break;
        case "tavily" when hasKey: builder.Services.AddTavilyWebSearchSkill(ws.ApiKey); break;
        case "duckduckgo":
        case "ddg":
        case "":
        default:
            builder.Services.AddDuckDuckGoWebSearchSkill();
            break;
    }
}
if (qy.EnableVisionSkill) builder.Services.AddVisionSkill();
if (qy.FileSystemSkill is { SandboxDirectory.Length: > 0 } fs)
    builder.Services.AddFileSystemSkill(fs.SandboxDirectory, fs.ReadOnly);
if (qy.CodeExecution is { Enabled: true, SandboxDirectory.Length: > 0 } cx)
    builder.Services.AddCodeExecutionSkill(new CodeExecutionOptions
    {
        SandboxDirectory = cx.SandboxDirectory,
        AllowedRuntimes = new HashSet<string>(cx.AllowedRuntimes, StringComparer.OrdinalIgnoreCase),
        PerCallTimeout = TimeSpan.FromSeconds(cx.TimeoutSeconds),
    });

// --- MCP external servers ---
foreach (var m in qy.McpServers)
{
    if (string.IsNullOrEmpty(m.ServerId) || string.IsNullOrEmpty(m.Command)) continue;
    builder.Services.AddMcpStdioServer(new McpStdioServerConfig
    {
        ServerId = m.ServerId,
        Command = m.Command,
        Arguments = m.Arguments,
        Environment = m.Environment,
    });
}

// --- default agent ---
builder.Services.AddReActAgent(new ReActAgentDefinition
{
    Id = qy.DefaultAgentId,
    Name = "QianYuan",
    Description = "General-purpose ReAct agent with progressive skill loading.",
    SystemPrompt =
        "你是 QianYuan（乾元）智能助手。遵循 ReAct 框架：先思考再行动，需要外部信息时调用工具，得到观察后继续推理。\n\n" +
        "关键技能使用指南：\n" +
        "• 当用户提及【规划/设计/需求/拆解/推理/评估】等关键词时 → 调用 brainstorming 技能进行深度分析与设计\n" +
        "• 当用户提及【查找技能/安装能力/扩展功能】等关键词时 → 调用 find-skills 技能查找合适的技能\n" +
        "• 当用户提及【创建/新建/制作技能】等关键词时 → 调用 skill-creator 技能帮助创建\n" +
        "• 当用户提及【总结/摘要/提炼】等关键词时 → 调用 summarize 技能\n" +
        "• 当用户提及【PDF/阅读文档】等关键词时 → 调用 pdf 技能处理PDF\n\n" +
        "工具会根据用户意图渐进式加载——只暴露当前可能用到的技能。",
    PreferredProviderId = qy.DefaultProviderId,
    Temperature = 0.4f,
    MaxIterations = qy.DefaultAgentMaxIterations,
    UseProgressiveSkillLoading = true,
    ProgressiveTopK = 8,
    PreloadSkills =
    [
        "skill.self.improving.agent",
        "skill.summarize",
        "skill.git.essentials",
        "skill.weather.query",
        "skill.api.gateway",
        "skill.agent.browser.automation",
        "skill.proactive.agent",
    ],
    Tags = ["default"],
});

// --- DingTalk ---
if (qy.DingTalk is { Enabled: true } dt)
{
    builder.Services.AddDingTalkIntegration(opts =>
    {
        opts.OutgoingWebhookUrl = dt.OutgoingWebhookUrl;
        opts.OutgoingSecret = dt.OutgoingSecret;
        opts.AppSecret = dt.AppSecret;
        opts.DefaultAgentId = dt.DefaultAgentId;
    });
}

// --- Provider model catalog (drives Web UI model selector) ---
builder.Services.AddSingleton<ProviderModelCatalog>();

// --- ASP.NET Core plumbing ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(qy.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// MCP server core (exposes our skills to external MCP clients).
builder.Services.AddSingleton(sp => sp.CreateMcpServerCore("qianyuan"));

var app = builder.Build();

// Mount skills and agents into the registries.
app.Services.RegisterProvidersFromServices(qy.DefaultProviderId);
app.Services.RegisterSkillsFromServices();
app.Services.RegisterMarkdownSkillsFromDirectories(qy.SkillDirectories.Select(d => new MarkdownSkillDirectoryOptions
{
    Path = d.Path,
    Recursive = d.Recursive,
    Enabled = d.Enabled,
    IdPrefix = d.IdPrefix,
}));
app.Services.MountMcpSkills();
app.Services.RegisterAgentsFromServices();

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.MapGet("/", () => Results.Ok(new
{
    name = "QianYuan Agentic Framework",
    version = "0.1.0",
    docs = "/swagger",
    endpoints = new
    {
        chatStream = "POST /api/chat/stream",
        chatHub = "/hubs/chat",
        agents = "GET /api/agents",
        skills = "GET /api/skills",
        providers = "GET /api/providers",
        sessions = "GET /api/sessions",
        mcpRpc = "POST /api/mcp",
        dingtalk = "POST /api/dingtalk/webhook",
    }
}));

app.Run();
