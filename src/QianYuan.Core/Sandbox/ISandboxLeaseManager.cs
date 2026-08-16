namespace QianYuan.Core.Sandbox;

/// <summary>
/// Allocates and releases sandbox leases used to isolate mutable execution
/// resources (temp files, working directories) for one tool call.
/// </summary>
public interface ISandboxLeaseManager
{
    ValueTask<SandboxLease> AcquireAsync(SandboxPolicySnapshot policy, CancellationToken ct = default);
    ValueTask ReleaseAsync(string leaseId, CancellationToken ct = default);
}

public sealed record SandboxLease(
    string LeaseId,
    string LeaseDirectory,
    DateTimeOffset ExpiresAt);
