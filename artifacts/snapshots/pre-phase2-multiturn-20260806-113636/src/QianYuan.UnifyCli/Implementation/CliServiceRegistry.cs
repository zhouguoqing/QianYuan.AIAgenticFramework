using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Implementation;

/// <summary>
/// Default implementation of the CLI service registry.
/// Manages registration, discovery, and lazy loading of CLI services.
/// </summary>
public sealed class CliServiceRegistry : ICliServiceRegistry
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CliServiceRegistry>? _logger;
    private readonly ConcurrentDictionary<string, ServiceEntry> _entries;

    private sealed class ServiceEntry
    {
        public required CliServiceManifest Manifest { get; init; }
        public Func<IServiceProvider, ICliService>? Factory { get; init; }
        public ICliService? Materialized { get; set; }
    }

    public CliServiceRegistry(IServiceProvider? services = null, ILogger<CliServiceRegistry>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
        _entries = new ConcurrentDictionary<string, ServiceEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public void Register(ICliService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));

        var manifest = new CliServiceManifest(
            service.Id,
            service.Name,
            service.Description,
            service.Tags,
            ApproximateMethodCount: 0,
            RequiresNetwork: true,
            RequiresAuthentication: service.DefaultAuthenticationProvider != null);

        var entry = new ServiceEntry
        {
            Manifest = manifest,
            Materialized = service
        };

        _entries[service.Id] = entry;
        _logger?.LogInformation("Registered CLI service '{ServiceId}'", service.Id);
    }

    public void Register(CliServiceManifest manifest, Func<IServiceProvider, ICliService> factory)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var entry = new ServiceEntry
        {
            Manifest = manifest,
            Factory = factory,
            Materialized = null
        };

        _entries[manifest.Id] = entry;
        _logger?.LogInformation("Registered CLI service manifest '{ServiceId}'", manifest.Id);
    }

    public async ValueTask<ICliService?> GetAsync(string serviceId, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(serviceId, out var entry))
        {
            return null;
        }

        if (entry.Materialized != null)
        {
            return entry.Materialized;
        }

        if (entry.Factory == null)
        {
            return null;
        }

        // Materialize the service
        var service = entry.Factory(_services);
        entry.Materialized = service;
        _logger?.LogInformation("Materialized CLI service '{ServiceId}'", serviceId);

        return service;
    }

    public IReadOnlyList<CliServiceManifest> ListManifests()
        => _entries.Values.Select(e => e.Manifest).ToArray();

    public ValueTask<IReadOnlyList<CliServiceManifest>> SearchAsync(IEnumerable<string> keywords, CancellationToken ct = default)
    {
        var keywordSet = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);

        var results = _entries.Values
            .Where(e => e.Manifest.Tags.Any(t => keywordSet.Contains(t)) ||
                        keywordSet.Any(k => e.Manifest.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                        keywordSet.Any(k => e.Manifest.Description.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Manifest)
            .ToArray();

        return new ValueTask<IReadOnlyList<CliServiceManifest>>(results);
    }
}
