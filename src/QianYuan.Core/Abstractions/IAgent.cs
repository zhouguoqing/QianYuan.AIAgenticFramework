using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Core.Abstractions;

/// <summary>
/// An agent encapsulates an LLM + a set of mounted skills + a runtime loop (ReAct, plan-and-execute, etc.).
/// Agents are registered into an <see cref="IAgentRegistry"/> and addressable by id.
/// </summary>
public interface IAgent
{
    /// <summary>Stable agent id (e.g. "qianyuan.default", "research", "coder").</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>Description used both for UI and for agent-as-tool delegation.</summary>
    string Description { get; }

    /// <summary>Optional list of tags for discovery.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>Streaming run - emits chunks until End/Error.</summary>
    IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest request, CancellationToken ct = default);
}

/// <summary>Request to run an agent.</summary>
public sealed class AgentRunRequest
{
    /// <summary>Conversation so far. The last message is typically the user turn.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Session id - used for memory, observability, and per-session skill state.</summary>
    public required string SessionId { get; init; }

    /// <summary>Override the agent's default model if non-null.</summary>
    public string? ModelOverride { get; init; }

    /// <summary>Override the provider id (e.g. force "claude" for this turn).</summary>
    public string? ProviderOverride { get; init; }

    /// <summary>Skill ids the caller explicitly wants enabled this turn (progressive load hint).</summary>
    public IReadOnlyList<string>? PreloadSkills { get; init; }

    /// <summary>Hard cap on ReAct iterations. Default decided by the agent.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Optional metadata bag (user id, tenant, locale, ...).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Registry for agents - allows agent-as-tool composition and dynamic discovery.</summary>
public interface IAgentRegistry
{
    /// <summary>Register an agent. Throws if id already present.</summary>
    void Register(IAgent agent);

    /// <summary>Resolve an agent by id. Returns null if not present.</summary>
    IAgent? Get(string id);

    /// <summary>List all known agents (for UI / discovery).</summary>
    IReadOnlyList<IAgent> List();
}
