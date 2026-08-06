namespace QianYuan.UnifyCli.Abstractions;

/// <summary>
/// Represents a CLI service that wraps one or more related HTTP endpoints.
/// Acts as a container for multiple CLI methods with shared configuration (base URI, auth, etc.).
/// </summary>
public interface ICliService
{
    /// <summary>Unique identifier for this service.</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Detailed description of the service.</summary>
    string Description { get; }

    /// <summary>Base URI for all HTTP calls in this service.</summary>
    string BaseUri { get; }

    /// <summary>
    /// Default authentication provider for all methods in this service.
    /// Individual methods can override this.
    /// </summary>
    IAuthenticationProvider? DefaultAuthenticationProvider { get; }

    /// <summary>
    /// Gets all CLI methods exposed by this service.
    /// </summary>
    ValueTask<IReadOnlyList<ICliMethod>> GetMethodsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a specific CLI method by ID.
    /// </summary>
    ValueTask<ICliMethod?> GetMethodAsync(string methodId, CancellationToken ct = default);

    /// <summary>
    /// Executes a CLI method with the given parameters.
    /// </summary>
    /// <param name="methodId">The ID of the method to execute.</param>
    /// <param name="parametersJson">The input parameters as JSON string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Execution result containing JSON content and optional summary.</returns>
    ValueTask<CliInvocationResult> InvokeAsync(string methodId, string parametersJson, CancellationToken ct = default);

    /// <summary>
    /// Tags for categorization and discovery.
    /// </summary>
    IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// Result of a CLI method invocation.
/// </summary>
public sealed class CliInvocationResult
{
    /// <summary>Machine-readable JSON the LLM will see.</summary>
    public required string JsonContent { get; init; }

    /// <summary>Optional human-friendly summary of the result.</summary>
    public string? HumanSummary { get; init; }

    /// <summary>True if the invocation failed.</summary>
    public bool IsError { get; init; }

    /// <summary>HTTP status code from the underlying request (if applicable).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Execution duration in milliseconds.</summary>
    public long ExecutionTimeMs { get; init; }

    public static CliInvocationResult Ok(string json, string? summary = null, int? statusCode = null, long executionTimeMs = 0) =>
        new() { JsonContent = json, HumanSummary = summary, IsError = false, StatusCode = statusCode, ExecutionTimeMs = executionTimeMs };

    public static CliInvocationResult Error(string message, int? statusCode = null, long executionTimeMs = 0) =>
        new()
        {
            JsonContent = $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(message)}}}",
            HumanSummary = message,
            IsError = true,
            StatusCode = statusCode,
            ExecutionTimeMs = executionTimeMs
        };
}
