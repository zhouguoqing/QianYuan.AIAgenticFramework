using QianYuan.Core.Abstractions;

namespace QianYuan.Kernel.Agents;

/// <summary>Default in-memory agent registry.</summary>
public sealed class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<string, IAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public void Register(IAgent agent)
    {
        lock (_gate)
        {
            if (_agents.ContainsKey(agent.Id))
                throw new InvalidOperationException($"Agent '{agent.Id}' already registered.");
            _agents[agent.Id] = agent;
        }
    }

    public IAgent? Get(string id)
    {
        lock (_gate) return _agents.TryGetValue(id, out var a) ? a : null;
    }

    public IReadOnlyList<IAgent> List()
    {
        lock (_gate) return _agents.Values.ToArray();
    }
}

/// <summary>Default in-memory LLM provider registry.</summary>
public sealed class LlmProviderRegistry : ILlmProviderRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ILlmProvider> _ordered = new();
    private readonly Lock _gate = new();
    private string? _defaultId;

    public void Register(ILlmProvider provider)
    {
        lock (_gate)
        {
            if (_providers.ContainsKey(provider.ProviderId))
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' already registered.");
            _providers[provider.ProviderId] = provider;
            _ordered.Add(provider);
            _defaultId ??= provider.ProviderId;
        }
    }

    public ILlmProvider? Get(string providerId)
    {
        lock (_gate) return _providers.TryGetValue(providerId, out var p) ? p : null;
    }

    public ILlmProvider Default
    {
        get
        {
            lock (_gate)
            {
                if (_defaultId is null || !_providers.TryGetValue(_defaultId, out var p))
                    throw new InvalidOperationException("No default LLM provider registered.");
                return p;
            }
        }
    }

    /// <summary>Explicitly set the default provider id.</summary>
    public void SetDefault(string providerId)
    {
        lock (_gate)
        {
            if (!_providers.ContainsKey(providerId))
                throw new InvalidOperationException($"Provider '{providerId}' is not registered.");
            _defaultId = providerId;
        }
    }

    public IReadOnlyList<ILlmProvider> List()
    {
        lock (_gate) return _ordered.ToArray();
    }
}
