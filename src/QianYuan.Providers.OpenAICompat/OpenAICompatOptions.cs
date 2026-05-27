namespace QianYuan.Providers.OpenAICompat;

/// <summary>
/// Options for an OpenAI Chat Completions compatible provider.
/// Same surface fits GPT (api.openai.com), Kimi (api.moonshot.cn), MiniMax,
/// Qwen-compat (dashscope-compat), DeepSeek, Together, OpenRouter, vLLM, etc.
/// </summary>
public sealed class OpenAICompatOptions
{
    /// <summary>Stable provider id used by Kernel routing, e.g. "openai", "kimi", "minimax", "qwen-compat".</summary>
    public required string ProviderId { get; init; } = "openai";

    /// <summary>API base URL. Examples:
    /// "https://api.openai.com/v1", "https://api.moonshot.cn/v1",
    /// "https://api.minimax.chat/v1", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1".
    /// </summary>
    public required string BaseUrl { get; init; }

    public required string ApiKey { get; init; }

    public required string DefaultModel { get; init; }

    /// <summary>If true, advertises vision capability (most chat-completions servers support image_url parts).</summary>
    public bool SupportsVision { get; init; } = true;

    /// <summary>If true, advertises tool/function calling.</summary>
    public bool SupportsTools { get; init; } = true;

    public bool SupportsParallelToolCalls { get; init; } = true;

    /// <summary>
    /// If false, omit temperature / top_p / max_tokens / stop in requests. Some endpoints (GPT-5.5,
    /// o-series reasoning models) reject any non-default sampling parameter.
    /// </summary>
    public bool SupportsSamplingParams { get; init; } = true;

    /// <summary>Optional org / project header.</summary>
    public string? Organization { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
