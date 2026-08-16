using Microsoft.Extensions.Hosting;

namespace QianYuan.Api.Services;

/// <summary>
/// Periodically cleans expired sandbox leases. This protects against leaked
/// lease directories if a request path is interrupted before normal release.
/// </summary>
public sealed class SandboxLeaseCleanupHostedService : BackgroundService
{
    private readonly InMemorySandboxLeaseManager _leaseManager;
    private readonly TimeSpan _interval;
    private readonly ILogger<SandboxLeaseCleanupHostedService> _logger;

    public SandboxLeaseCleanupHostedService(
        InMemorySandboxLeaseManager leaseManager,
        TimeSpan interval,
        ILogger<SandboxLeaseCleanupHostedService> logger)
    {
        _leaseManager = leaseManager;
        _interval = interval;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                var removed = await _leaseManager.CleanupExpiredAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (removed > 0)
                {
                    _logger.LogInformation(
                        "Sandbox lease cleanup removed {Removed} expired leases. Active leases: {Active}",
                        removed,
                        _leaseManager.ActiveLeaseCount);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sandbox lease cleanup cycle failed");
            }
        }
    }
}
