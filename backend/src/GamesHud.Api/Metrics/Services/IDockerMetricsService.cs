namespace GamesHud.Api.Metrics.Services;

public interface IDockerMetricsService
{
    Task<DockerSummaryMetrics> GetSummaryMetricsAsync(CancellationToken cancellationToken);

    Task<ContainerMetrics?> GetContainerMetricsAsync(
        string containerId,
        CancellationToken cancellationToken);
}
