namespace QianYuan.Providers.AzureOpenAI;

/// <summary>
/// Options for Azure OpenAI Service. The request/response wire format is OpenAI Chat Completions,
/// but the URL is keyed by deployment name and the api-version query parameter, and authentication
/// uses the <c>api-key</c> header instead of the standard <c>Authorization: Bearer</c>.
/// </summary>
public sealed class AzureOpenAIOptions
{
    /// <summary>Stable provider id used by Kernel routing, defaults to <c>"azure-openai"</c>.</summary>
    public string ProviderId { get; init; } = "azure-openai";

    /// <summary>
    /// Azure OpenAI resource endpoint, e.g. <c>https://my-resource.openai.azure.com</c>.
    /// Do NOT include <c>/openai</c> or any path - it's appended automatically.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>API key from the Azure Portal (key1 / key2).</summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Default deployment name (Azure equivalent of "model"). Used when the request does not
    /// specify a model, or when the requested model name has no entry in <see cref="ModelToDeployment"/>.
    /// </summary>
    public required string DefaultDeployment { get; init; }

    /// <summary>REST API version, e.g. <c>2024-10-21</c> or <c>2024-12-01-preview</c>.</summary>
    public string ApiVersion { get; init; } = "2024-10-21";

    /// <summary>
    /// Optional mapping from logical model id (as sent in <c>ChatRequest.Options.Model</c>) to the
    /// Azure deployment name. If a requested model is not in the map, it is used as the deployment
    /// name directly (a common convention when admins name deployments after the underlying model).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ModelToDeployment { get; init; }

    /// <summary>If true, advertises vision capability.</summary>
    public bool SupportsVision { get; init; } = true;

    /// <summary>If true, advertises tool/function calling.</summary>
    public bool SupportsTools { get; init; } = true;

    public bool SupportsParallelToolCalls { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
