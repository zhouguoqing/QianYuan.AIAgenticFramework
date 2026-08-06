using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QianYuan.UnifyCli.Abstractions;
using QianYuan.UnifyCli.Implementation;
using QianYuan.UnifyCli.Skills;

namespace QianYuan.UnifyCli;

/// <summary>
/// Extension methods for adding UnifyCli to the service collection.
/// </summary>
public static class UnifyCliExtensions
{
    /// <summary>
    /// Adds the UnifyCli infrastructure to the service collection.
    /// </summary>
    public static IServiceCollection AddUnifyCli(this IServiceCollection services)
    {
        services.TryAddSingleton<IAuthenticationProviderFactory, AuthenticationProviderFactory>();
        services.TryAddSingleton<ICliServiceRegistry, CliServiceRegistry>();
        services.TryAddSingleton<CliServiceSkillFactory>();
        services.TryAddSingleton<UnifyHttpClient>();
        return services;
    }

    /// <summary>
    /// Registers a CLI service in the registry.
    /// </summary>
    public static IServiceCollection AddCliService(this IServiceCollection services, ICliService cliService)
    {
        if (cliService == null) throw new ArgumentNullException(nameof(cliService));

        services.AddUnifyCli();
        services.AddSingleton<ICliService>(cliService);

        // Register in registry
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<ICliServiceRegistry>();
            registry.Register(cliService);
            return cliService;
        });

        return services;
    }

    /// <summary>
    /// Registers a CLI service with lazy initialization.
    /// </summary>
    public static IServiceCollection AddCliService(
        this IServiceCollection services,
        CliServiceManifest manifest,
        Func<IServiceProvider, ICliService> factory)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        services.AddUnifyCli();
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<ICliServiceRegistry>();
            registry.Register(manifest, factory);
            return manifest;
        });

        return services;
    }

    /// <summary>
    /// Registers a CLI service skill for an existing CLI service.
    /// </summary>
    public static IServiceCollection AddCliServiceSkill(this IServiceCollection services, string cliServiceId)
    {
        services.AddUnifyCli();
        // Note: Skill registration happens through the ISkill interface
        // Individual CLI service skills should be registered separately if needed
        return services;
    }
}
