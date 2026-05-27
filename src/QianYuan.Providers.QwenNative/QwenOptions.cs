namespace QianYuan.Providers.QwenNative;

public sealed class QwenOptions
{
    public string ProviderId { get; init; } = "qwen";
    public string BaseUrl { get; init; } = "https://dashscope.aliyuncs.com";
    public required string ApiKey { get; init; }
    public required string DefaultModel { get; init; } = "qwen-max";

    /// <summary>If true, requests SSE streaming with incremental delta output.</summary>
    public bool IncrementalOutput { get; init; } = true;

    /// <summary>Switch to multimodal endpoint when the model id starts with 'qwen-vl'.</summary>
    public bool AutoDetectMultimodal { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
