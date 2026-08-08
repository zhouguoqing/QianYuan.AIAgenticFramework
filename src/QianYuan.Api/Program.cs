using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using QianYuan.Api.Configuration;
using QianYuan.Api.Hubs;
using QianYuan.Api.Services;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Data;
using QianYuan.Data.Services;
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
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
var qy = builder.Configuration.GetSection("QianYuan").Get<QianYuanApiOptions>() ?? new();
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new();

// --- Database and encryption ---
var dbConnectionString = builder.Configuration.GetConnectionString("QianYuanDb") 
    ?? "DataSource=qianyuan.db";
var dbProvider = builder.Configuration["Database:Provider"];
var encryptionKey = builder.Configuration["QianYuan:EncryptionKey"] 
    ?? AesEncryptionService.GenerateEncryptionKey();
builder.Services.AddQianYuanData(dbConnectionString, encryptionKey, dbProvider);
builder.Services.AddScoped<IAgentManagementService, AgentManagementService>();
builder.Services.AddScoped<IAgentExecutionService, AgentExecutionService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICreditService, CreditService>();
builder.Services.AddScoped<IWorkTaskService, WorkTaskService>();
builder.Services.AddScoped<IExpertTeamService, ExpertTeamService>();
builder.Services.AddSingleton<IExpertTeamTemplateService, ExpertTeamTemplateService>();
builder.Services.AddScoped<ISkillMarketplaceService, SkillMarketplaceService>();
builder.Services.AddSingleton<IMemoryService, LocalMemoryService>();
builder.Services.AddSingleton<IWorkTaskExecutionHarness, WorkTaskExecutionHarness>();
builder.Services.AddSingleton<IExpertCatalogService, ExpertCatalogService>();
builder.Services.AddScoped<ICustomExpertService, CustomExpertService>();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// --- core kernel ---
builder.Services.AddQianYuanKernel();
builder.Services.AddConversationMemorySkill();

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
// Web search: DuckDuckGo needs no key 鈥?use it whenever the configured provider is "duckduckgo"
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
        "浣犳槸 QianYuan锛堜咕鍏冿級鏅鸿兘鍔╂墜銆傞伒寰?ReAct 妗嗘灦锛氬厛鎬濊€冨啀琛屽姩锛岄渶瑕佸閮ㄤ俊鎭椂璋冪敤宸ュ叿锛屽緱鍒拌瀵熷悗缁х画鎺ㄧ悊銆俓n\n" +
        "鍏抽敭鎶€鑳戒娇鐢ㄦ寚鍗楋細\n" +
        "鈥?褰撶敤鎴锋彁鍙娿€愯鍒?璁捐/闇€姹?鎷嗚В/鎺ㄧ悊/璇勪及銆戠瓑鍏抽敭璇嶆椂 鈫?璋冪敤 brainstorming 鎶€鑳借繘琛屾繁搴﹀垎鏋愪笌璁捐\n" +
        "鈥?褰撶敤鎴锋彁鍙娿€愭煡鎵炬妧鑳?瀹夎鑳藉姏/鎵╁睍鍔熻兘銆戠瓑鍏抽敭璇嶆椂 鈫?璋冪敤 find-skills 鎶€鑳芥煡鎵惧悎閫傜殑鎶€鑳絓n" +
        "鈥?褰撶敤鎴锋彁鍙娿€愬垱寤?鏂板缓/鍒朵綔鎶€鑳姐€戠瓑鍏抽敭璇嶆椂 鈫?璋冪敤 skill-creator 鎶€鑳藉府鍔╁垱寤篭n" +
        "鈥?褰撶敤鎴锋彁鍙娿€愭€荤粨/鎽樿/鎻愮偧銆戠瓑鍏抽敭璇嶆椂 鈫?璋冪敤 summarize 鎶€鑳絓n" +
        "鈥?褰撶敤鎴锋彁鍙娿€怭DF/闃呰鏂囨。銆戠瓑鍏抽敭璇嶆椂 鈫?璋冪敤 pdf 鎶€鑳藉鐞哖DF\n\n" +
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
        "qianyuan.memory",
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

// --- Knowledge services ---
builder.Services.AddSingleton<QianYuan.Api.Services.IKnowledgeDocumentParser, QianYuan.Api.Services.KnowledgeDocumentParser>();
var knowledgeStoreType = (qy.KnowledgeStore?.Provider ?? "inmemory").Trim().ToLowerInvariant();
if (knowledgeStoreType is "postgres" or "pgvector" or "pg")
{
    builder.Services.AddSingleton<QianYuan.Api.Services.IKnowledgeStore>(sp =>
        new QianYuan.Api.Services.PostgresKnowledgeStore(
            qy.KnowledgeStore?.Postgres ?? new QianYuan.Api.Configuration.PostgresOptions(),
            sp.GetRequiredService<ILlmProviderRegistry>(),
            sp.GetRequiredService<ILogger<QianYuan.Api.Services.PostgresKnowledgeStore>>()));
}
else
{
    builder.Services.AddSingleton<QianYuan.Api.Services.IKnowledgeStore, QianYuan.Api.Services.VectorKnowledgeStore>();
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(qy.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// MCP server core (exposes our skills to external MCP clients).
builder.Services.AddSingleton(sp => sp.CreateMcpServerCore("qianyuan"));

var app = builder.Build();

// Initialize database
await app.Services.InitializeDatabaseAsync();

// Mount skills and agents into the registries.
app.Services.RegisterProvidersFromServices(qy.DefaultProviderId);
app.Services.RegisterSkillsFromServices();
app.Services.RegisterMarkdownSkillsFromDirectories(qy.SkillDirectories.Select(d => new MarkdownSkillDirectoryOptions
{
    Path = ResolveSkillDirectory(d.Path, builder.Environment.ContentRootPath),
    Recursive = d.Recursive,
    Enabled = d.Enabled,
    IdPrefix = d.IdPrefix,
}));
app.Services.MountMcpSkills();
await InitializeSkillMarketplaceAsync(app.Services);
app.Services.RegisterAgentsFromServices();

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
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


static async Task InitializeSkillMarketplaceAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var marketplace = scope.ServiceProvider.GetRequiredService<ISkillMarketplaceService>();
    await marketplace.InitializeAsync();
}

static string ResolveSkillDirectory(string configuredPath, string contentRootPath)
{
    var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
    if (Path.IsPathRooted(expanded)) return Path.GetFullPath(expanded);

    foreach (var basePath in CandidateBasePaths(contentRootPath))
    {
        var candidate = Path.GetFullPath(Path.Combine(basePath, expanded));
        if (Directory.Exists(candidate)) return candidate;
    }

    return Path.GetFullPath(expanded);
}

static IEnumerable<string> CandidateBasePaths(string contentRootPath)
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var root in new[] { Directory.GetCurrentDirectory(), contentRootPath, AppContext.BaseDirectory })
    {
        var current = root;
        while (!string.IsNullOrWhiteSpace(current) && seen.Add(current))
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }
    }
}