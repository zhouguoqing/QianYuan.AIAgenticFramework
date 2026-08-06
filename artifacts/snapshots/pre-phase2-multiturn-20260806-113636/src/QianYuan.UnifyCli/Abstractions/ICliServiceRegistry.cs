namespace QianYuan.UnifyCli.Abstractions;

/// <summary>
/// Manages registration and discovery of CLI services.
/// </summary>
public interface ICliServiceRegistry
{
    /// <summary>
    /// Registers a CLI service.
    /// </summary>
    /// <param name="service">The CLI service to register.</param>
    void Register(ICliService service);

    /// <summary>
    /// Registers a CLI service by manifest and factory.
    /// Allows lazy initialization of services.
    /// </summary>
    /// <param name="manifest">The service manifest.</param>
    /// <param name="factory">Factory function to create the service on demand.</param>
    void Register(CliServiceManifest manifest, Func<IServiceProvider, ICliService> factory);

    /// <summary>
    /// Gets a service by ID.
    /// </summary>
    /// <param name="serviceId">The service ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The CLI service, or null if not found.</returns>
    ValueTask<ICliService?> GetAsync(string serviceId, CancellationToken ct = default);

    /// <summary>
    /// Gets all registered service manifests.
    /// This is a lightweight operation that doesn't require materializing all services.
    /// </summary>
    /// <returns>List of service manifests.</returns>
    IReadOnlyList<CliServiceManifest> ListManifests();

    /// <summary>
    /// Gets all services whose methods match the given tags or keywords.
    /// </summary>
    /// <param name="keywords">Search keywords.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matching service manifests.</returns>
    ValueTask<IReadOnlyList<CliServiceManifest>> SearchAsync(IEnumerable<string> keywords, CancellationToken ct = default);
}

/// <summary>
/// Lightweight manifest for a CLI service, used during discovery and progressive loading.
/// </summary>
public sealed record CliServiceManifest(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    int ApproximateMethodCount,
    bool RequiresNetwork,
    bool RequiresAuthentication);
