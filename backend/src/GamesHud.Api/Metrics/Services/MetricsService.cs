namespace GamesHud.Api.Metrics.Services;

public sealed class MetricsService : IMetricsService
{
    private readonly IHostMetricsService _hostMetricsService;
    private readonly IDockerMetricsService _dockerMetricsService;

    public MetricsService(
        IHostMetricsService hostMetricsService,
        IDockerMetricsService dockerMetricsService)
    {
        _hostMetricsService = hostMetricsService;
        _dockerMetricsService = dockerMetricsService;
    }

    public Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        return _hostMetricsService.GetHostMetricsAsync(cancellationToken);
    }

    public Task<DockerSummaryMetrics> GetDockerSummaryMetricsAsync(CancellationToken cancellationToken)
    {
        return _dockerMetricsService.GetSummaryMetricsAsync(cancellationToken);
    }

    public Task<ContainerMetrics?> GetContainerMetricsAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        return _dockerMetricsService.GetContainerMetricsAsync(containerId, cancellationToken);
    }
}
