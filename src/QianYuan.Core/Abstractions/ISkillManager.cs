using QianYuan.Core.Models;

namespace QianYuan.Core.Abstractions;

/// <summary>
/// Manages the catalog of skills installed in the kernel. Supports progressive (lazy) loading:
/// callers can enumerate cheap manifests, then materialize specific skills on demand.
/// </summary>
public interface ISkillManager
{
    /// <summary>All known skill manifests. Cheap to call - does not instantiate skills.</summary>
    IReadOnlyList<SkillManifest> ListManifests();

    /// <summary>Resolve a skill by id. Materializes it (loading resources) if not yet loaded.</summary>
    ValueTask<ISkill> GetAsync(string skillId, CancellationToken ct = default);

    /// <summary>Bulk variant of <see cref="GetAsync"/>.</summary>
    ValueTask<IReadOnlyList<ISkill>> GetManyAsync(IEnumerable<string> skillIds, CancellationToken ct = default);

    /// <summary>
    /// Pick skills relevant to a query/intent. Used by ReAct planner to gate which tools are exposed
    /// to the model on a given turn (keeps the tool list small and focused).
    /// </summary>
    ValueTask<IReadOnlyList<SkillManifest>> SelectRelevantAsync(string intent, int topK = 8, CancellationToken ct = default);

    /// <summary>Register a skill (or its factory) into the catalog.</summary>
    void Register(ISkill skill);
    void Register(SkillManifest manifest, Func<IServiceProvider, ISkill> factory);

    /// <summary>Aggregate the tool definitions of the given skill ids (used to build LLM tool list).</summary>
    ValueTask<IReadOnlyList<ToolDefinition>> CollectToolsAsync(IEnumerable<string> skillIds, CancellationToken ct = default);

    /// <summary>
    /// Toggle a registered skill on or off. Disabled skills remain in <see cref="ListManifests"/>
    /// (so management UIs can still see them) but contribute no tools — <see cref="CollectToolsAsync"/>
    /// and <see cref="SelectRelevantAsync"/> skip them. No-ops if the id is unknown.
    /// </summary>
    void SetEnabled(string skillId, bool enabled);

    /// <summary>Whether the skill currently contributes tools. Unknown ids return false.</summary>
    bool IsEnabled(string skillId);
}
