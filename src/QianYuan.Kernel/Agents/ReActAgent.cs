using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Kernel.ReAct;
using QianYuan.Kernel.Tools;

namespace QianYuan.Kernel.Agents;

/// <summary>
/// Default ReAct-based agent. Wraps a ReActEngine with a fixed personality and skill manifest.
/// </summary>
public sealed class ReActAgent : IAgent
{
    private readonly ILlmProviderRegistry _providers;
    private readonly ISkillManager _skills;
    private readonly IAgentRegistry _agents;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly ReActAgentDefinition _def;

    public ReActAgent(
        ReActAgentDefinition definition,
        ILlmProviderRegistry providers,
        ISkillManager skills,
        IAgentRegistry agents,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _def = definition;
        _providers = providers;
        _skills = skills;
        _agents = agents;
        _loggerFactory = loggerFactory;
        _services = services;
    }

    public string Id => _def.Id;
    public string Name => _def.Name;
    public string Description => _def.Description;
    public IReadOnlyList<string> Tags => _def.Tags;

    public async IAsyncEnumerable<StreamingChunk> RunAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var provider = (request.ProviderOverride is not null ? _providers.Get(request.ProviderOverride) : null)
                       ?? (_def.PreferredProviderId is not null ? _providers.Get(_def.PreferredProviderId) : null)
                       ?? _providers.Default;

        var engine = new ReActEngine(
            provider,
            _skills,
            _agents,
            _loggerFactory.CreateLogger<ReActEngine>(),
            new ReActEngineOptions
            {
                MaxIterations = request.MaxIterations ?? _def.MaxIterations,
                Model = request.ModelOverride ?? _def.PreferredModel,
                Temperature = _def.Temperature,
                MaxOutputTokens = _def.MaxOutputTokens,
                UseProgressiveSelection = _def.UseProgressiveSkillLoading,
                ProgressiveTopK = _def.ProgressiveTopK,
                ExposeAgentsAsTools = _def.ExposeAgentsAsTools,
                LoopEngineering = _def.LoopEngineering,
            });

        // Build per-turn dispatcher with a tool-name -> skill-id resolver from the engine's live map.
        // We use a closure over a shared dictionary updated by the engine through CollectToolsAsync.
        var resolver = new ToolResolver(_skills, _def.PreloadSkills);
        await resolver.PrimeAsync(ct).ConfigureAwait(false);

        var dispatcher = new ToolDispatcher(
            _skills, _agents, _loggerFactory.CreateLogger<ToolDispatcher>(),
            toolName => resolver.Resolve(toolName));

        var req = new ReActRunRequest
        {
            InitialMessages = request.Messages,
            SessionId = request.SessionId,
            Services = _services,
            Dispatcher = dispatcher,
            SelfAgentId = Id,
            SystemPrompt = _def.SystemPrompt,
            PreloadSkills = request.PreloadSkills ?? _def.PreloadSkills,
            MaxIterations = request.MaxIterations ?? _def.MaxIterations,
            Metadata = request.Metadata,
        };

        await foreach (var chunk in engine.RunAsync(req, ct).ConfigureAwait(false))
        {
            yield return chunk with { AgentId = Id };
        }
    }

    /// <summary>Caches tool-name -> skill-id mappings across the conversation.</summary>
    private sealed class ToolResolver
    {
        private readonly ISkillManager _sm;
        private readonly IReadOnlyList<string>? _preload;
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        public ToolResolver(ISkillManager sm, IReadOnlyList<string>? preload)
        {
            _sm = sm;
            _preload = preload;
        }

        public async Task PrimeAsync(CancellationToken ct)
        {
            var ids = _preload ?? _sm.ListManifests().Select(m => m.Id);
            var tools = await _sm.CollectToolsAsync(ids, ct).ConfigureAwait(false);
            foreach (var t in tools)
                if (t.SkillId is not null) _map[t.Name] = t.SkillId;
        }

        public string? Resolve(string toolName)
        {
            if (_map.TryGetValue(toolName, out var s)) return s;
            // Slow path: scan all skills.
            foreach (var manifest in _sm.ListManifests())
            {
                var skill = _sm.GetAsync(manifest.Id).AsTask().GetAwaiter().GetResult();
                var tools = skill.GetToolsAsync().AsTask().GetAwaiter().GetResult();
                foreach (var t in tools)
                {
                    _map[t.Name] = manifest.Id;
                }
                if (_map.TryGetValue(toolName, out var s2)) return s2;
            }
            return null;
        }
    }
}

public sealed class ReActAgentDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? SystemPrompt { get; init; }
    public string? PreferredProviderId { get; init; }
    public string? PreferredModel { get; init; }
    public float? Temperature { get; init; } = 0.4f;
    public int? MaxOutputTokens { get; init; }
    public int MaxIterations { get; init; } = 100;

    public bool UseProgressiveSkillLoading { get; init; } = true;
    public int ProgressiveTopK { get; init; } = 8;
    public bool ExposeAgentsAsTools { get; init; } = true;

    public LoopEngineeringOptions LoopEngineering { get; init; } = new();

    /// <summary>If set, only these skills are mounted (progressive selection still allowed within).</summary>
    public IReadOnlyList<string>? PreloadSkills { get; init; }
}
