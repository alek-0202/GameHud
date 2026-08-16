using GamesHud.Api.Metrics.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Metrics.Services;

public sealed class MetricsSnapshotCollector : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MetricsOptions> _options;
    private readonly IMetricsHistoryStore _historyStore;
    private readonly ILogger<MetricsSnapshotCollector> _logger;

    public MetricsSnapshotCollector(
        IServiceScopeFactory scopeFactory,
        IOptions<MetricsOptions> options,
        IMetricsHistoryStore historyStore,
        ILogger<MetricsSnapshotCollector> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _historyStore = historyStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectSnapshotAsync(stoppingToken);

        using var timer = new PeriodicTimer(GetSnapshotInterval());

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CollectSnapshotAsync(stoppingToken);
        }
    }

    private async Task CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var metricsService = scope.ServiceProvider.GetRequiredService<IMetricsService>();
            var palworldMetricsService = scope.ServiceProvider.GetRequiredService<IPalworldMetricsService>();
            var hostMetrics = await metricsService.GetHostMetricsAsync(cancellationToken);
            var palworldMetrics = await palworldMetricsService.GetPalworldMetricsAsync(cancellationToken);

            _historyStore.Add(
                new MetricSnapshot(
                    DateTimeOffset.UtcNow,
                    hostMetrics.CpuPercent,
                    hostMetrics.MemoryUsedBytes,
                    hostMetrics.MemoryTotalBytes,
                    hostMetrics.DiskUsedBytes,
                    hostMetrics.DiskTotalBytes,
                    palworldMetrics.CpuPercent,
                    palworldMetrics.MemoryUsageBytes,
                    palworldMetrics.MemoryLimitBytes,
                    palworldMetrics.PlayersOnline),
                GetRetention());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Metrics snapshot collection failed.");
        }
    }

    private TimeSpan GetSnapshotInterval()
    {
        var seconds = _options.Value.SnapshotIntervalSeconds;

        return TimeSpan.FromSeconds(seconds is >= 10 and <= 3600 ? seconds : 60);
    }

    private TimeSpan GetRetention()
    {
        var hours = _options.Value.RetentionHours;

        return TimeSpan.FromHours(hours is >= 1 and <= 168 ? hours : 24);
    }
}
