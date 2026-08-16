namespace GamesHud.Api.Metrics.Services;

public interface IHostMetricsService
{
    Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken);
}
