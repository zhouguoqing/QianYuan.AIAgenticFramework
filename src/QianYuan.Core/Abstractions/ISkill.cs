using QianYuan.Core.Models;

namespace QianYuan.Core.Abstractions;

/// <summary>
/// A skill is a cohesive bundle of tools (plus optional system-prompt fragments) that an agent can mount.
/// Skills support progressive loading: a manifest is cheap to enumerate, while the full set of tools
/// (and any embeddings/resources) is only materialized on demand.
/// </summary>
public interface ISkill
{
    /// <summary>Stable, unique identifier - e.g. "qianyuan.websearch".</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>One-line description used to gate progressive loading by intent.</summary>
    string Description { get; }

    /// <summary>Tags used for filtering / discovery.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>Optional system-prompt snippet contributed to the agent when the skill is active.</summary>
    string? SystemPromptFragment { get; }

    /// <summary>Returns the tools exposed by this skill. May be lazily computed.</summary>
    ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default);

    /// <summary>Invoke a tool exposed by this skill. <paramref name="argumentsJson"/> is the JSON arg payload.</summary>
    ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default);
}

/// <summary>Per-call ambient state passed to a skill invocation.</summary>
public sealed class SkillInvocationContext
{
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public required IServiceProvider Services { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Outcome of a tool invocation.</summary>
public sealed class SkillInvocationResult
{
    /// <summary>Machine-readable JSON the LLM will see.</summary>
    public required string JsonContent { get; init; }

    /// <summary>Optional human summary surfaced to the UI as an observation.</summary>
    public string? HumanSummary { get; init; }

    /// <summary>True if the call failed - the LLM will be told and may retry.</summary>
    public bool IsError { get; init; }

    public static SkillInvocationResult Ok(string json, string? summary = null) =>
        new() { JsonContent = json, HumanSummary = summary };

    public static SkillInvocationResult Error(string message) =>
        new() { JsonContent = $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(message)}}}", HumanSummary = message, IsError = true };
}

/// <summary>
/// Lightweight manifest used during progressive loading. A skill catalog can list manifests cheaply
/// without instantiating every skill (which may load embeddings, open connections, etc.).
/// </summary>
public sealed record SkillManifest(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    int ApproximateToolCount,
    bool RequiresNetwork,
    bool RequiresFilesystem);
