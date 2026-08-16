namespace GamesHud.Api.Metrics.Services;

public interface IMetricsService
{
    Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken);

    Task<DockerSummaryMetrics> GetDockerSummaryMetricsAsync(CancellationToken cancellationToken);

    Task<ContainerMetrics?> GetContainerMetricsAsync(
        string containerId,
        CancellationToken cancellationToken);
}
