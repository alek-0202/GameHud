using System.Globalization;
using GamesHud.Api.Metrics.Contracts;

namespace GamesHud.Api.Metrics.Services;

public static class MetricsContractMapper
{
    public static HostMetricsResponse Map(HostMetrics metrics)
    {
        return new HostMetricsResponse(
            metrics.CpuPercent,
            metrics.MemoryUsedBytes,
            metrics.MemoryTotalBytes,
            metrics.DiskUsedBytes,
            metrics.DiskTotalBytes,
            metrics.UptimeSeconds,
            metrics.RetrievedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public static DockerSummaryMetricsResponse Map(DockerSummaryMetrics metrics)
    {
        return new DockerSummaryMetricsResponse(
            metrics.RunningContainers,
            metrics.StoppedContainers);
    }

    public static ContainerMetricsResponse Map(ContainerMetrics metrics)
    {
        return new ContainerMetricsResponse(
            metrics.ContainerId,
            metrics.Name,
            metrics.CpuPercent,
            metrics.MemoryUsageBytes,
            metrics.MemoryLimitBytes,
            metrics.MemoryPercent,
            metrics.RetrievedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public static PalworldMetricsResponse Map(
        PalworldMetrics metrics,
        IReadOnlyCollection<MetricSnapshot> history)
    {
        return new PalworldMetricsResponse(
            metrics.ContainerName,
            metrics.CpuPercent,
            metrics.MemoryUsageBytes,
            metrics.MemoryLimitBytes,
            metrics.MemoryPercent,
            metrics.UptimeSeconds,
            metrics.PlayersOnline,
            metrics.MaxPlayers,
            history.Select(Map).ToArray(),
            metrics.RetrievedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public static MetricHistoryPointResponse Map(MetricSnapshot snapshot)
    {
        return new MetricHistoryPointResponse(
            snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            snapshot.HostCpuPercent,
            snapshot.HostMemoryUsedBytes,
            snapshot.HostMemoryTotalBytes,
            snapshot.DiskUsedBytes,
            snapshot.DiskTotalBytes,
            snapshot.PalworldCpuPercent,
            snapshot.PalworldMemoryUsageBytes,
            snapshot.PalworldMemoryLimitBytes,
            snapshot.PlayersOnline);
    }
}
