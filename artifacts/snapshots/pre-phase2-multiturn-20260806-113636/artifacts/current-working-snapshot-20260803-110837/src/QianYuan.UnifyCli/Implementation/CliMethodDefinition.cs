using QianYuan.UnifyCli.Abstractions;
using System.Text.Json.Nodes;

namespace QianYuan.UnifyCli.Implementation;

/// <summary>
/// Default implementation of a CLI method backed by an HTTP endpoint.
/// </summary>
public sealed class CliMethodDefinition : ICliMethod
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersSchema { get; init; }
    public string? ResponseSchema { get; init; }
    public required string BaseUri { get; init; }
    public required string HttpMethod { get; init; }
    public required string PathTemplate { get; init; }
    public IReadOnlyDictionary<string, string>? QueryParams { get; init; }
    public string? RequestBodyTemplate { get; init; }
    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }
    public IResponseTransformer? ResponseTransformer { get; init; }
    public IAuthenticationProvider? AuthenticationProvider { get; init; }
    public int TimeoutMs { get; init; } = 30000;
    public int RetryCount { get; init; } = 1;
    public int RetryDelayMs { get; init; } = 100;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Simple response transformer that extracts a specific field from the response.
/// </summary>
public sealed class JsonPathResponseTransformer : IResponseTransformer
{
    private readonly string _jsonPath;

    /// <summary>
    /// Creates a transformer that extracts a field using a simple JSON path.
    /// e.g., "$.data.result" will extract the result field from within data.
    /// </summary>
    public JsonPathResponseTransformer(string jsonPath)
    {
        _jsonPath = jsonPath;
    }

    public Task<string> TransformAsync(int statusCode, string content, IReadOnlyDictionary<string, string> headers)
    {
        try
        {
            var jsonNode = JsonNode.Parse(content);
            if (jsonNode == null) return Task.FromResult(content);

            // Simple JSONPath navigation - handles dot notation
            var parts = _jsonPath.Split(new[] { '$', '.' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (jsonNode is JsonObject obj)
                {
                    jsonNode = obj[part];
                }
                else
                {
                    return Task.FromResult(content);
                }
            }

            return Task.FromResult(jsonNode?.ToJsonString() ?? "null");
        }
        catch
        {
            // If transformation fails, return original content
            return Task.FromResult(content);
        }
    }
}
