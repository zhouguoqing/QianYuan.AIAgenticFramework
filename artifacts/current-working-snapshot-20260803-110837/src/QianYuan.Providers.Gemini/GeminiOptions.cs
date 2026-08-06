namespace QianYuan.Providers.Gemini;

public sealed class GeminiOptions
{
    public string ProviderId { get; init; } = "gemini";
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com";
    public string ApiVersion { get; init; } = "v1beta";
    public required string ApiKey { get; init; }
    public required string DefaultModel { get; init; } = "gemini-2.0-flash";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
