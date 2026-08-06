namespace QianYuan.Providers.Anthropic;

public sealed class AnthropicOptions
{
    public string ProviderId { get; init; } = "claude";
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
    public required string ApiKey { get; init; }
    public required string DefaultModel { get; init; } = "claude-opus-4-7";
    public string AnthropicVersion { get; init; } = "2023-06-01";
    public bool EnableExtendedThinking { get; init; } = false;
    public int? ThinkingBudgetTokens { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
