using QianYuan.Api.Configuration;

namespace QianYuan.Api.Services;

public sealed class SandboxWorkerHealthProbeHostedService : BackgroundService
{
    private readonly IReadOnlyList<HttpCodeExecutionWorkerClient> _remotes;
    private readonly SandboxWorkerHealthStateCache _healthCache;
    private readonly SandboxWorkerOptions _options;
    private readonly ILogger<SandboxWorkerHealthProbeHostedService> _logger;

    public SandboxWorkerHealthProbeHostedService(
        IReadOnlyList<HttpCodeExecutionWorkerClient> remotes,
        SandboxWorkerHealthStateCache healthCache,
        SandboxWorkerOptions options,
        ILogger<SandboxWorkerHealthProbeHostedService> logger)
    {
        _remotes = remotes;
        _healthCache = healthCache;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_remotes.Count == 0)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(3, _options.HealthCheckIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var remote in _remotes)
            {
                var healthy = await remote.CheckHealthAsync(_options.HealthPath, stoppingToken).ConfigureAwait(false);
                _healthCache.MarkProbeResult(remote.TargetId, healthy);

                if (!healthy)
                {
                    _logger.LogWarning("Sandbox worker health probe failed. WorkerId={WorkerId}", remote.TargetId);
                }
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
