using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using System.Globalization;

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
        var normalizedIntent = NormalizeKey(intent);
        SkillManifest[] all;
        lock (_gate) all = _entries.Values.Where(e => e.Enabled).Select(e => e.Manifest).ToArray();

        var ranked = all
            .Select(m => (m, score: Score(m, tokens, normalizedIntent)))
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

    private static int Score(SkillManifest m, HashSet<string> tokens, string normalizedIntent)
    {
        var s = 0;
        foreach (var tag in m.Tags) if (tokens.Contains(tag)) s += 3;
        foreach (var word in Tokenize(m.Id)) if (tokens.Contains(word)) s += 3;
        foreach (var word in Tokenize(m.Name)) if (tokens.Contains(word)) s += 2;
        foreach (var word in Tokenize(m.Description)) if (tokens.Contains(word)) s += 1;

        s += ScoreWellKnownPromptSkill(m, normalizedIntent);

        return s;
    }

    private static int ScoreWellKnownPromptSkill(SkillManifest manifest, string normalizedIntent)
    {
        if (string.IsNullOrWhiteSpace(normalizedIntent)) return 0;

        var score = 0;
        if (IsSkill(manifest, "using-superpowers")) score += 1;

        if (HasAny(normalizedIntent, "plan", "planning", "decompose", "decomposition", "breakdown", "reasoning", "react", "design", "requirement", "evaluate", "implement", "计划", "规划", "拆解", "推理", "设计", "需求", "评估", "改进", "实现"))
        {
            if (IsSkill(manifest, "brainstorm", "brainstorming")) score += 16;
        }

        if (HasAny(normalizedIntent, "skill", "skills", "findskill", "findskills", "installskill", "技能", "安装", "下载", "引用", "查找", "寻找", "扩展能力"))
        {
            if (IsSkill(manifest, "find-skills")) score += 16;
        }

        if (HasAny(normalizedIntent, "createskill", "skillcreator", "skillmd", "newskill", "创建技能", "新建技能", "编写技能", "制作技能"))
        {
            if (IsSkill(manifest, "skill-creator")) score += 16;
        }

        if (HasAny(normalizedIntent, "pdf", "阅读pdf", "pdf阅读"))
        {
            if (IsSkill(manifest, "pdf")) score += 16;
        }

        if (HasAny(normalizedIntent, "summary", "summarize", "summarise", "recap", "总结", "摘要", "提炼", "归纳"))
        {
            if (IsSkill(manifest, "summarize")) score += 16;
        }

        return score;
    }

    private static bool IsSkill(SkillManifest manifest, params string[] names)
    {
        var id = NormalizeKey(manifest.Id);
        var name = NormalizeKey(manifest.Name);
        foreach (var candidate in names.Select(NormalizeKey))
        {
            if (id.EndsWith(candidate, StringComparison.OrdinalIgnoreCase) || name == candidate)
                return true;
        }
        return false;
    }

    private static bool HasAny(string normalizedText, params string[] terms)
        => terms.Any(term => normalizedText.Contains(NormalizeKey(term), StringComparison.OrdinalIgnoreCase));

    private static string NormalizeKey(string text)
    {
        var chars = text.ToLower(CultureInfo.InvariantCulture)
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }
}
