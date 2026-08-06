using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Kernel.Agents;
using QianYuan.Kernel.ReAct;
using QianYuan.Kernel.Skills;

namespace QianYuan.Kernel;

/// <summary>
/// Single entry point used by hosts (WebAPI, console sample, integrations) to wire up the framework.
/// Adds the registries, default in-memory session store, skill manager, and an empty agent registry.
/// </summary>
public static class QianYuanKernelExtensions
{
    public static IServiceCollection AddQianYuanKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<ILlmProviderRegistry, LlmProviderRegistry>();
        services.TryAddSingleton<IAgentRegistry, AgentRegistry>();
        services.TryAddSingleton<ISkillManager, SkillManager>();
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
        services.TryAddSingleton<ITokenCounter, HeuristicTokenCounter>();
        return services;
    }

    /// <summary>Register a ReAct-based agent built from a definition.</summary>
    public static IServiceCollection AddReActAgent(
        this IServiceCollection services,
        ReActAgentDefinition definition)
    {
        services.AddSingleton<IAgent>(sp => new ReActAgent(
            definition,
            sp.GetRequiredService<ILlmProviderRegistry>(),
            sp.GetRequiredService<ISkillManager>(),
            sp.GetRequiredService<IAgentRegistry>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp));
        return services;
    }

    /// <summary>
    /// Resolve all <see cref="IAgent"/> instances from DI and register them into the agent registry.
    /// Call this once at application startup before serving requests.
    /// </summary>
    public static void RegisterAgentsFromServices(this IServiceProvider sp)
    {
        var registry = sp.GetRequiredService<IAgentRegistry>();
        foreach (var agent in sp.GetServices<IAgent>())
        {
            if (registry.Get(agent.Id) is null) registry.Register(agent);
        }
    }

    /// <summary>
    /// Force-resolve all <see cref="ILlmProvider"/> services so each provider extension's
    /// auto-registration into <see cref="ILlmProviderRegistry"/> actually fires. Optionally
    /// promote a configured provider id to be the default.
    /// </summary>
    public static void RegisterProvidersFromServices(this IServiceProvider sp, string? defaultProviderId = null)
    {
        // Touching the IEnumerable triggers each AddSingleton<ILlmProvider> factory.
        foreach (var _ in sp.GetServices<ILlmProvider>()) { }

        if (!string.IsNullOrWhiteSpace(defaultProviderId))
        {
            var registry = sp.GetRequiredService<ILlmProviderRegistry>();
            if (registry is LlmProviderRegistry concrete && registry.Get(defaultProviderId) is not null)
                concrete.SetDefault(defaultProviderId);
        }
    }
}

internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly Dictionary<string, SessionState> _store = new();
    private readonly Lock _gate = new();

    public ValueTask<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate)
            return ValueTask.FromResult(_store.TryGetValue(sessionId, out var s) ? s : null);
    }

    public ValueTask SaveAsync(SessionState state, CancellationToken ct = default)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        lock (_gate) _store[state.SessionId] = state;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate) _store.Remove(sessionId);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SessionSummary>> ListAsync(string? ownerId = null, int take = 50, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<SessionSummary> list = _store.Values
                .Where(s => ownerId is null || s.OwnerId == ownerId)
                .OrderByDescending(s => s.UpdatedAt)
                .Take(take)
                .Select(s => new SessionSummary(s.SessionId, s.Title, s.AgentId, s.Messages.Count, s.CreatedAt, s.UpdatedAt))
                .ToArray();
            return ValueTask.FromResult(list);
        }
    }
}
