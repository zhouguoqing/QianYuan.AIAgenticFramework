using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Kernel.Skills;

/// <summary>
/// Default <see cref="ISkillManager"/> implementation with progressive (lazy) loading.
///
/// Two registration modes:
///  1. <see cref="Register(ISkill)"/> - already-constructed skill, materialized immediately.
///  2. <see cref="Register(SkillManifest, Func{IServiceProvider, ISkill})"/> - manifest + factory.
///     The factory is invoked the first time the skill is actually requested.
///
/// Selection (<see cref="SelectRelevantAsync"/>) uses a lightweight keyword/tag scoring against the intent string -
/// good enough as a default; users can plug in their own implementation for semantic search.
/// </summary>
public sealed class SkillManager : ISkillManager
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SkillManager> _logger;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private sealed class Entry
    {
        public required SkillManifest Manifest { get; init; }
        public Func<IServiceProvider, ISkill>? Factory { get; init; }
        public ISkill? Materialized { get; set; }
        public IReadOnlyList<ToolDefinition>? CachedTools { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public SkillManager(IServiceProvider services, ILogger<SkillManager> logger)
    {
        _services = services;
        _logger = logger;
    }

    public IReadOnlyList<SkillManifest> ListManifests()
    {
        lock (_gate) return _entries.Values.Select(e => e.Manifest).ToArray();
    }

    public void Register(ISkill skill)
    {
        var manifest = new SkillManifest(
            skill.Id,
            skill.Name,
            skill.Description,
            skill.Tags,
            ApproximateToolCount: 0,
            RequiresNetwork: false,
            RequiresFilesystem: false);

        lock (_gate)
        {
            _entries[skill.Id] = new Entry { Manifest = manifest, Materialized = skill };
        }
    }

    public void Register(SkillManifest manifest, Func<IServiceProvider, ISkill> factory)
    {
        lock (_gate)
        {
            _entries[manifest.Id] = new Entry { Manifest = manifest, Factory = factory };
        }
    }

    public async ValueTask<ISkill> GetAsync(string skillId, CancellationToken ct = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(skillId, out var e))
                throw new Core.Exceptions.SkillNotFoundException(skillId);
            entry = e;
        }

        if (entry.Materialized is not null) return entry.Materialized;

        // Materialize outside the lock - skill construction may take time.
        var skill = entry.Factory!(_services);
        lock (_gate)
        {
            entry.Materialized = skill;
        }
        _logger.LogInformation("Materialized skill {SkillId}", skillId);
        return skill;
    }

    public async ValueTask<IReadOnlyList<ISkill>> GetManyAsync(IEnumerable<string> skillIds, CancellationToken ct = default)
    {
        var list = new List<ISkill>();
        foreach (var id in skillIds)
            list.Add(await GetAsync(id, ct).ConfigureAwait(false));
        return list;
    }

    public ValueTask<IReadOnlyList<SkillManifest>> SelectRelevantAsync(string intent, int topK = 8, CancellationToken ct = default)
    {
        var tokens = Tokenize(intent);
        SkillManifest[] all;
        lock (_gate) all = _entries.Values.Where(e => e.Enabled).Select(e => e.Manifest).ToArray();

        var ranked = all
            .Select(m => (m, score: Score(m, tokens)))
            .Where(t => t.score > 0)
            .OrderByDescending(t => t.score)
            .Take(topK)
            .Select(t => t.m)
            .ToArray();

        // When nothing matches, return the first `topK` manifests so the agent still has something to look at.
        IReadOnlyList<SkillManifest> result = ranked.Length > 0 ? ranked : all.Take(topK).ToArray();
        return ValueTask.FromResult(result);
    }

    public async ValueTask<IReadOnlyList<ToolDefinition>> CollectToolsAsync(IEnumerable<string> skillIds, CancellationToken ct = default)
    {
        var output = new List<ToolDefinition>();
        foreach (var id in skillIds)
        {
            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(id, out var e)) continue;
                if (!e.Enabled) continue;
                entry = e;
            }
            if (entry.CachedTools is not null)
            {
                output.AddRange(entry.CachedTools);
                continue;
            }
            var skill = await GetAsync(id, ct).ConfigureAwait(false);
            var tools = await skill.GetToolsAsync(ct).ConfigureAwait(false);
            var tagged = tools.Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                JsonSchema = t.JsonSchema,
                SkillId = t.SkillId ?? id,
            }).ToArray();
            lock (_gate) entry.CachedTools = tagged;
            output.AddRange(tagged);
        }
        return output;
    }

    public void SetEnabled(string skillId, bool enabled)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(skillId, out var e)) e.Enabled = enabled;
        }
    }

    public bool IsEnabled(string skillId)
    {
        lock (_gate)
            return _entries.TryGetValue(skillId, out var e) && e.Enabled;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in text.Split([' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 2) set.Add(token);
        }
        return set;
    }

    private static int Score(SkillManifest m, HashSet<string> tokens)
    {
        var s = 0;
        foreach (var tag in m.Tags) if (tokens.Contains(tag)) s += 3;
        foreach (var word in Tokenize(m.Name)) if (tokens.Contains(word)) s += 2;
        foreach (var word in Tokenize(m.Description)) if (tokens.Contains(word)) s += 1;
        return s;
    }
}
