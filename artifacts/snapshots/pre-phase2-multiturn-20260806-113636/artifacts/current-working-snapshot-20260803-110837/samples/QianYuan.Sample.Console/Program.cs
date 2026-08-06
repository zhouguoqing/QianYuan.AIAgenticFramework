using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Kernel;
using QianYuan.Kernel.Agents;
using QianYuan.Providers.OpenAICompat;
using QianYuan.Skills.Builtin;

// End-to-end console sample: runs the default ReAct agent against an OpenAI-compatible provider.
// Configure via env:
//   QIANYUAN_BASEURL  (default https://api.openai.com/v1)
//   QIANYUAN_APIKEY   (required to actually call out)
//   QIANYUAN_MODEL    (default gpt-4o-mini)
//   TAVILY_API_KEY    (optional - enables web search skill)

var host = Host.CreateApplicationBuilder(args);
host.Configuration.AddEnvironmentVariables();
host.Logging.SetMinimumLevel(LogLevel.Information);

host.Services.AddQianYuanKernel();

var apiKey = host.Configuration["QIANYUAN_APIKEY"] ?? "";
host.Services.AddOpenAICompatProvider(new OpenAICompatOptions
{
    ProviderId = "openai",
    BaseUrl = host.Configuration["QIANYUAN_BASEURL"] ?? "https://api.openai.com/v1",
    ApiKey = apiKey,
    DefaultModel = host.Configuration["QIANYUAN_MODEL"] ?? "gpt-4o-mini",
});

var tavily = host.Configuration["TAVILY_API_KEY"];
if (!string.IsNullOrEmpty(tavily))
    host.Services.AddTavilyWebSearchSkill(tavily);

host.Services.AddReActAgent(new ReActAgentDefinition
{
    Id = "qianyuan.default",
    Name = "QianYuan",
    Description = "Console sample agent",
    SystemPrompt = "你是 QianYuan 智能体，按 ReAct 思考：需要外部信息时调用工具，简洁回答。",
    Temperature = 0.4f,
});

var app = host.Build();
app.Services.RegisterSkillsFromServices();
app.Services.RegisterAgentsFromServices();

if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("[警告] QIANYUAN_APIKEY 未设置, 仅展示注册结构, 不实际调用模型。");
}

var registry = app.Services.GetRequiredService<IAgentRegistry>();
var agent = registry.Get("qianyuan.default")!;

Console.WriteLine("注册的 Agent:");
foreach (var a in registry.List()) Console.WriteLine($"  - {a.Id} : {a.Description}");
Console.WriteLine();

var skills = app.Services.GetRequiredService<ISkillManager>();
Console.WriteLine("注册的 Skill:");
foreach (var s in skills.ListManifests()) Console.WriteLine($"  - {s.Id} : {s.Description}");
Console.WriteLine();

if (string.IsNullOrEmpty(apiKey)) return;

Console.Write("> ");
var line = Console.ReadLine();
while (!string.IsNullOrWhiteSpace(line))
{
    var run = new AgentRunRequest
    {
        Messages = new[] { ChatMessage.User(line) },
        SessionId = "console",
    };

    await foreach (var c in agent.RunAsync(run))
    {
        if (c.Kind == StreamingChunkKind.TextDelta && c.Text is not null)
            Console.Write(c.Text);
        else if (c.Kind == StreamingChunkKind.ToolCallStart)
            Console.Write($"\n[tool:{c.ToolName}] ");
        else if (c.Kind == StreamingChunkKind.ToolObservation)
            Console.Write($"\n[obs] {c.Text}\n");
        else if (c.Kind == StreamingChunkKind.Error)
            Console.WriteLine($"\n[error] {c.Text}");
    }
    Console.WriteLine();
    Console.Write("> ");
    line = Console.ReadLine();
}
