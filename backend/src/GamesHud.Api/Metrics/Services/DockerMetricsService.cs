using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Metrics.Services;

public sealed class DockerMetricsService : IDockerMetricsService
{
    private readonly IOptions<DockerOptions> _options;
    private readonly IContainerService _containerService;

    public DockerMetricsService(
        IOptions<DockerOptions> options,
        IContainerService containerService)
    {
        _options = options;
        _containerService = containerService;
    }

    public async Task<DockerSummaryMetrics> GetSummaryMetricsAsync(CancellationToken cancellationToken)
    {
        var containers = await _containerService.GetContainersAsync(cancellationToken);

        return new DockerSummaryMetrics(
            containers.Count(container => string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase)),
            containers.Count(container => IsStopped(container.State)));
    }

    public async Task<ContainerMetrics?> GetContainerMetricsAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);

            if (container.State?.Running != true)
            {
                return new ContainerMetrics(
                    container.ID ?? containerId,
                    NormalizeContainerName(container.Name) ?? containerId,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow);
            }

            var stats = await GetStatsAsync(client, containerId, cancellationToken);

            return MapStats(
                container.ID ?? containerId,
                NormalizeContainerName(container.Name) ?? containerId,
                stats);
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    private IDockerClient CreateClient()
    {
        var endpoint = _options.Value.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new DockerClientConfiguration().CreateClient();
        }

        return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
    }

    private static async Task<ContainerStatsResponse> GetStatsAsync(
        IDockerClient client,
        string containerId,
        CancellationToken cancellationToken)
    {
        ContainerStatsResponse? stats = null;
        var progress = new Progress<ContainerStatsResponse>(value => stats = value);

        await client.Containers.GetContainerStatsAsync(
            containerId,
            new ContainerStatsParameters
            {
                Stream = false,
                OneShot = true
            },
            progress,
            cancellationToken);

        return stats
            ?? throw new DockerUnavailableException("Docker Engine returned no container stats.");
    }

    private static ContainerMetrics MapStats(
        string containerId,
        string containerName,
        ContainerStatsResponse stats)
    {
        var cpuPercent = MetricsCalculator.CalculateDockerCpuPercent(
            stats.CPUStats?.CPUUsage?.TotalUsage ?? 0,
            stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0,
            stats.CPUStats?.SystemUsage ?? 0,
            stats.PreCPUStats?.SystemUsage ?? 0,
            stats.CPUStats?.OnlineCPUs ?? 0);
        ulong? memoryUsage = stats.MemoryStats is null
            ? null
            : MetricsCalculator.CalculateContainerMemoryUsage(
                stats.MemoryStats.Usage,
                stats.MemoryStats.Stats);
        var memoryLimit = stats.MemoryStats?.Limit;

        return new ContainerMetrics(
            containerId,
            containerName,
            cpuPercent,
            memoryUsage,
            memoryLimit,
            DateTimeOffset.UtcNow);
    }

    private static bool IsStopped(string state)
    {
        return state.Equals("created", StringComparison.OrdinalIgnoreCase)
            || state.Equals("exited", StringComparison.OrdinalIgnoreCase)
            || state.Equals("stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeContainerName(string? name)
    {
        return name?.Trim().TrimStart('/');
    }

    private static bool IsContainerNotFound(Exception exception)
    {
        return exception is DockerContainerNotFoundException
            || exception is DockerApiException { StatusCode: HttpStatusCode.NotFound };
    }

    private static bool IsDockerAccessFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (IsContainerNotFound(exception))
        {
            return false;
        }

        return exception is DockerApiException
            or DockerUnavailableException
            or HttpRequestException
            or IOException
            or TimeoutException
            or TaskCanceledException
            or UriFormatException
            or UnauthorizedAccessException;
    }
}
