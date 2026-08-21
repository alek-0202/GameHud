namespace GamesHud.Api.Metrics.Services;

public sealed class UnsupportedHostMetricsService : IHostMetricsService
{
    public Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        throw new HostMetricsUnavailableException(
            "Host metrics are not supported for this platform yet.");
    }
}
