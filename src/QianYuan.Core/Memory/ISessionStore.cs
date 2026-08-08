namespace QianYuan.Core.Memory;

/// <summary>
/// Lightweight session/conversation memory abstraction.
/// Implementations: in-memory (default), Redis, SQL, vector-store, etc.
/// </summary>
public interface ISessionStore
{
    ValueTask<SessionState?> GetAsync(string sessionId, CancellationToken ct = default);
    ValueTask SaveAsync(SessionState state, CancellationToken ct = default);
    ValueTask DeleteAsync(string sessionId, CancellationToken ct = default);
    ValueTask<int> ClearAsync(string? ownerId = null, CancellationToken ct = default);
    ValueTask<IReadOnlyList<SessionSummary>> ListAsync(string? ownerId = null, int take = 50, CancellationToken ct = default);
}

public sealed class SessionState
{
    public required string SessionId { get; init; }
    public string? OwnerId { get; init; }
    public string? Title { get; set; }
    public string? AgentId { get; set; }
    public List<Models.ChatMessage> Messages { get; init; } = new();
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record SessionSummary(
    string SessionId,
    string? Title,
    string? AgentId,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
