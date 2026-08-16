namespace QianYuan.Core.Sandbox;

/// <summary>
/// Strongly typed per-call sandbox context. Carries the session/workspace facts
/// that mutating skills need without forcing them to read generic metadata bags.
/// </summary>
public sealed record SandboxPolicySnapshot
{
    public string? OwnerId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string? WorkspaceId { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? WorkspaceLabel { get; init; }
    public string? Permission { get; init; }
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
    public string? LeaseId { get; init; }
    public string? LeaseDirectory { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    public bool AllowsWrite => IsWritablePermission(Permission);

    public static bool IsWritablePermission(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return true;

        return permission.Trim().ToLowerInvariant() switch
        {
            "read-only" or "readonly" or "read" or "false" or "0" => false,
            "workspace-write" or "workspace_write" or "write" or "full" or "danger-full-access" => true,
            _ => true,
        };
    }
}