namespace QianYuan.Core.Sandbox;

/// <summary>
/// Worker execution request for sandboxed code runtime.
/// </summary>
public sealed record CodeExecutionWorkerRequest
{
    public required string Runtime { get; init; }
    public required string Code { get; init; }
    public required string WorkingDirectory { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required int MaxOutputChars { get; init; }

    public string? LeaseId { get; init; }
    public string? SessionId { get; init; }
    public string? OwnerId { get; init; }
    public string? WorkspaceId { get; init; }
    public int Attempt { get; init; } = 1;
}

/// <summary>
/// Worker execution result for sandboxed code runtime.
/// </summary>
public sealed record CodeExecutionWorkerResponse
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public string WorkerId { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public long DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Client contract for sandbox worker execution.
/// </summary>
public interface ICodeExecutionWorkerClient
{
    ValueTask<CodeExecutionWorkerResponse> ExecuteAsync(CodeExecutionWorkerRequest request, CancellationToken ct = default);
}
