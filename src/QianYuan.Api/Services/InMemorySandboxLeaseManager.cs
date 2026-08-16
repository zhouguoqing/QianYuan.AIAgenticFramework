using System.Collections.Concurrent;
using QianYuan.Core.Sandbox;

namespace QianYuan.Api.Services;

/// <summary>
/// Local process lease manager for mutable sandbox execution. This is the first
/// step toward a distributed lease service: it scopes each tool call under a
/// unique lease directory and eagerly cleans it on release.
/// </summary>
public sealed class InMemorySandboxLeaseManager : ISandboxLeaseManager
{
    private readonly string _root;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, SandboxLease> _leases = new(StringComparer.Ordinal);

    public InMemorySandboxLeaseManager(string rootDirectory, TimeSpan? ttl = null)
    {
        _root = Path.GetFullPath(rootDirectory);
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        Directory.CreateDirectory(_root);
    }

    public ValueTask<SandboxLease> AcquireAsync(SandboxPolicySnapshot policy, CancellationToken ct = default)
    {
        var owner = Sanitize(policy.OwnerId ?? "anonymous");
        var workspace = Sanitize(policy.WorkspaceId ?? policy.WorkspaceLabel ?? "default-workspace");
        var session = Sanitize(string.IsNullOrWhiteSpace(policy.SessionId) ? "session" : policy.SessionId);
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseDir = Path.Combine(_root, "users", owner, "workspaces", workspace, "sessions", session, "leases", leaseId);
        var leaseFullPath = Path.GetFullPath(leaseDir);

        if (!leaseFullPath.StartsWith(_root, StringComparison.Ordinal))
            throw new InvalidOperationException("resolved lease path escapes sandbox root");

        Directory.CreateDirectory(leaseFullPath);
        var lease = new SandboxLease(leaseId, leaseFullPath, DateTimeOffset.UtcNow.Add(_ttl));
        _leases[leaseId] = lease;
        return ValueTask.FromResult(lease);
    }

    public int ActiveLeaseCount => _leases.Count;

    public ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var removed = 0;
        foreach (var pair in _leases)
        {
            ct.ThrowIfCancellationRequested();
            if (pair.Value.ExpiresAt > now) continue;
            if (_leases.TryRemove(pair.Key, out var lease))
            {
                CleanupDirectory(lease.LeaseDirectory);
                removed++;
            }
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask ReleaseAsync(string leaseId, CancellationToken ct = default)
    {
        if (_leases.TryRemove(leaseId, out var lease))
            CleanupDirectory(lease.LeaseDirectory);

        return ValueTask.CompletedTask;
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is best effort; leases are already released from memory.
        }
    }

    private static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "unknown";

        Span<char> buffer = stackalloc char[Math.Min(trimmed.Length, 120)];
        var idx = 0;
        foreach (var ch in trimmed)
        {
            if (idx >= buffer.Length) break;
            buffer[idx++] = char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_';
        }

        var sanitized = new string(buffer[..idx]).Trim('.');
        return sanitized.Length == 0 ? "unknown" : sanitized;
    }
}
