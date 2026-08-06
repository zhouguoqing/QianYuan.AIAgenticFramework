namespace QianYuan.UnifyCli.Abstractions;

/// <summary>
/// Represents a CLI method that wraps an HTTP service endpoint or operation.
/// Each CLI method corresponds to a tool that can be exposed to agents via Skills.
/// </summary>
public interface ICliMethod
{
    /// <summary>Unique identifier for this CLI method.</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Detailed description of what this method does.</summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema (draft 2020-12 compatible) describing the input parameters.
    /// Should be a complete JSON Schema object.
    /// </summary>
    string ParametersSchema { get; }

    /// <summary>
    /// JSON Schema (draft 2020-12 compatible) describing the return value.
    /// Should be a complete JSON Schema object or empty string for any result.
    /// </summary>
    string? ResponseSchema { get; }

    /// <summary>
    /// Base URI for the HTTP service (e.g., "https://api.example.com").
    /// </summary>
    string BaseUri { get; }

    /// <summary>
    /// HTTP method: "GET", "POST", "PUT", "DELETE", "PATCH", etc.
    /// </summary>
    string HttpMethod { get; }

    /// <summary>
    /// URL path template relative to BaseUri (e.g., "/v1/users/{userId}").
    /// Supports parameter interpolation using {paramName} syntax.
    /// </summary>
    string PathTemplate { get; }

    /// <summary>
    /// Optional query parameters to include in the request.
    /// Keys are parameter names, values are parameter values from input or literal strings.
    /// </summary>
    IReadOnlyDictionary<string, string>? QueryParams { get; }

    /// <summary>
    /// Optional request body template for POST/PUT/PATCH.
    /// If null, parameters are passed as query string or in URL.
    /// If a string starting with "$", it indicates a body from a parameter.
    /// Otherwise it's treated as a literal body or JSON template.
    /// </summary>
    string? RequestBodyTemplate { get; }

    /// <summary>
    /// Optional request header modifications.
    /// Can include content-type, accept, and other custom headers.
    /// </summary>
    IReadOnlyDictionary<string, string>? RequestHeaders { get; }

    /// <summary>
    /// Optional response transformation.
    /// If provided, applies JSON transformation to the response before returning.
    /// </summary>
    IResponseTransformer? ResponseTransformer { get; }

    /// <summary>
    /// Authentication provider for this specific method.
    /// If null, will use the parent service's default authentication.
    /// </summary>
    IAuthenticationProvider? AuthenticationProvider { get; }

    /// <summary>
    /// Timeout in milliseconds for this HTTP request. Default: 30000 (30 seconds).
    /// </summary>
    int TimeoutMs { get; }

    /// <summary>
    /// Number of retries on transient failures. Default: 1.
    /// </summary>
    int RetryCount { get; }

    /// <summary>
    /// Delay between retries in milliseconds. Default: 100.
    /// </summary>
    int RetryDelayMs { get; }

    /// <summary>
    /// Tags for categorization and discovery.
    /// </summary>
    IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// Transforms the HTTP response into the final CLI result.
/// </summary>
public interface IResponseTransformer
{
    /// <summary>
    /// Transforms the response content.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="content">Response body content.</param>
    /// <param name="headers">Response headers.</param>
    /// <returns>Transformed response as JSON string.</returns>
    Task<string> TransformAsync(int statusCode, string content, IReadOnlyDictionary<string, string> headers);
}
