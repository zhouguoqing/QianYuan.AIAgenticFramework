namespace QianYuan.Api.Configuration;

/// <summary>Root configuration section bound from appsettings.json (or env vars).</summary>
public sealed class QianYuanApiOptions
{
    public string DefaultAgentId { get; set; } = "qianyuan.default";
    public string? DefaultProviderId { get; set; }
    public int DefaultAgentMaxIterations { get; set; } = 100;

    public OpenAIProviderOptions[] OpenAICompatProviders { get; set; } = Array.Empty<OpenAIProviderOptions>();
    public AzureOpenAIProviderOptions[] AzureOpenAIProviders { get; set; } = Array.Empty<AzureOpenAIProviderOptions>();
    public AnthropicProviderOptions? Anthropic { get; set; }
    public GeminiProviderOptions? Gemini { get; set; }
    public QwenProviderOptions? Qwen { get; set; }

    public WebSearchOptions? WebSearch { get; set; }
    public FileSystemSkillOptions? FileSystemSkill { get; set; }
    public bool EnableVisionSkill { get; set; } = true;
    public CodeSkillOptions? CodeExecution { get; set; }
    public SkillDirectoryOptions[] SkillDirectories { get; set; } = Array.Empty<SkillDirectoryOptions>();

    public McpServerEntry[] McpServers { get; set; } = Array.Empty<McpServerEntry>();
    public DingTalkConfig? DingTalk { get; set; }
    public CorsConfig Cors { get; set; } = new();
}

public sealed class OpenAIProviderOptions
{
    public string ProviderId { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "gpt-4o";
    public string? ImageModel { get; set; }
    public bool SupportsVision { get; set; } = true;
    /// <summary>
    /// Set false for endpoints that reject non-default temperature / top_p (e.g. GPT-5.5, o-series).
    /// </summary>
    public bool SupportsSamplingParams { get; set; } = true;
    /// <summary>Selectable models surfaced to the UI. DefaultModel is auto-prepended if absent.</summary>
    public string[] Models { get; set; } = Array.Empty<string>();
}

public sealed class AzureOpenAIProviderOptions
{
    public string ProviderId { get; set; } = "azure-openai";
    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com (no path).</summary>
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>Default deployment name (Azure equivalent of "model").</summary>
    public string DefaultDeployment { get; set; } = "";
    /// <summary>REST API version, e.g. 2024-10-21 or 2024-12-01-preview.</summary>
    public string ApiVersion { get; set; } = "2024-10-21";
    public bool SupportsVision { get; set; } = true;
    public bool SupportsTools { get; set; } = true;
    public bool SupportsParallelToolCalls { get; set; } = true;
    /// <summary>Optional logical-model → Azure deployment name overrides.</summary>
    public Dictionary<string, string> ModelToDeployment { get; set; } = new();
    /// <summary>Selectable logical models surfaced to the UI. Keys of ModelToDeployment are also merged in.</summary>
    public string[] Models { get; set; } = Array.Empty<string>();
}

public sealed class AnthropicProviderOptions
{
    public string ProviderId { get; set; } = "claude";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "claude-opus-4-7";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public bool EnableExtendedThinking { get; set; }
    public int? ThinkingBudgetTokens { get; set; }
    public string[] Models { get; set; } = Array.Empty<string>();
}

public sealed class GeminiProviderOptions
{
    public string ProviderId { get; set; } = "gemini";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "gemini-2.0-flash";
    public string[] Models { get; set; } = Array.Empty<string>();
}

public sealed class QwenProviderOptions
{
    public string ProviderId { get; set; } = "qwen";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "qwen-max";
    public string[] Models { get; set; } = Array.Empty<string>();
}

public sealed class WebSearchOptions
{
    public string Provider { get; set; } = "tavily";
    public string ApiKey { get; set; } = "";
}

public sealed class FileSystemSkillOptions
{
    public string SandboxDirectory { get; set; } = "";
    public bool ReadOnly { get; set; }
}

public sealed class CodeSkillOptions
{
    public string SandboxDirectory { get; set; } = "";
    public bool Enabled { get; set; }
    public string[] AllowedRuntimes { get; set; } = ["python", "node"];
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class SkillDirectoryOptions
{
    public string Path { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string IdPrefix { get; set; } = "skill";
}

public sealed class McpServerEntry
{
    public string ServerId { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Environment { get; set; } = new();
}

public sealed class DingTalkConfig
{
    public bool Enabled { get; set; }
    public string? OutgoingWebhookUrl { get; set; }
    public string? OutgoingSecret { get; set; }
    public string? AppSecret { get; set; }
    public string DefaultAgentId { get; set; } = "qianyuan.default";
}

public sealed class CorsConfig
{
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173", "http://localhost:3000"];
}
