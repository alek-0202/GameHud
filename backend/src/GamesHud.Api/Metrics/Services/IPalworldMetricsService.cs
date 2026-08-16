namespace GamesHud.Api.Metrics.Services;

public interface IPalworldMetricsService
{
    Task<PalworldMetrics> GetPalworldMetricsAsync(CancellationToken cancellationToken);
}
