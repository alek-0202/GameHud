namespace GamesHud.Api.Metrics.Services;

public sealed record HostMetrics(
    double? CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    ulong DiskUsedBytes,
    ulong DiskTotalBytes,
    double UptimeSeconds,
    DateTimeOffset RetrievedAt);

public sealed record ContainerMetrics(
    string ContainerId,
    string Name,
    double? CpuPercent,
    ulong? MemoryUsageBytes,
    ulong? MemoryLimitBytes,
    DateTimeOffset RetrievedAt)
{
    public double? MemoryPercent => MemoryUsageBytes is null || MemoryLimitBytes is null || MemoryLimitBytes == 0
        ? null
        : Math.Round(MemoryUsageBytes.Value / (double)MemoryLimitBytes.Value * 100, 2);
}

public sealed record DockerSummaryMetrics(
    int RunningContainers,
    int StoppedContainers);

public sealed record PalworldMetrics(
    string ContainerName,
    double? CpuPercent,
    ulong? MemoryUsageBytes,
    ulong? MemoryLimitBytes,
    int? UptimeSeconds,
    int? PlayersOnline,
    int? MaxPlayers,
    DateTimeOffset RetrievedAt)
{
    public double? MemoryPercent => MemoryUsageBytes is null || MemoryLimitBytes is null || MemoryLimitBytes == 0
        ? null
        : Math.Round(MemoryUsageBytes.Value / (double)MemoryLimitBytes.Value * 100, 2);
}

public sealed record MetricSnapshot(
    DateTimeOffset Timestamp,
    double? HostCpuPercent,
    ulong? HostMemoryUsedBytes,
    ulong? HostMemoryTotalBytes,
    ulong? DiskUsedBytes,
    ulong? DiskTotalBytes,
    double? PalworldCpuPercent,
    ulong? PalworldMemoryUsageBytes,
    ulong? PalworldMemoryLimitBytes,
    int? PlayersOnline);
