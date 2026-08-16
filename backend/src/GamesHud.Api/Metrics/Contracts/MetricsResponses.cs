namespace GamesHud.Api.Metrics.Contracts;

public sealed record HostMetricsResponse(
    double? CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    ulong DiskUsedBytes,
    ulong DiskTotalBytes,
    double UptimeSeconds,
    string RetrievedAt);

public sealed record DockerSummaryMetricsResponse(
    int RunningContainers,
    int StoppedContainers);

public sealed record SystemMetricsResponse(
    HostMetricsResponse Host,
    DockerSummaryMetricsResponse Docker,
    IReadOnlyCollection<MetricHistoryPointResponse> History);

public sealed record ContainerMetricsResponse(
    string ContainerId,
    string Name,
    double? CpuPercent,
    ulong? MemoryUsageBytes,
    ulong? MemoryLimitBytes,
    double? MemoryPercent,
    string RetrievedAt);

public sealed record PalworldMetricsResponse(
    string ContainerName,
    double? CpuPercent,
    ulong? MemoryUsageBytes,
    ulong? MemoryLimitBytes,
    double? MemoryPercent,
    int? UptimeSeconds,
    int? PlayersOnline,
    int? MaxPlayers,
    IReadOnlyCollection<MetricHistoryPointResponse> History,
    string RetrievedAt);

public sealed record MetricHistoryPointResponse(
    string Timestamp,
    double? HostCpuPercent,
    ulong? HostMemoryUsedBytes,
    ulong? HostMemoryTotalBytes,
    ulong? DiskUsedBytes,
    ulong? DiskTotalBytes,
    double? PalworldCpuPercent,
    ulong? PalworldMemoryUsageBytes,
    ulong? PalworldMemoryLimitBytes,
    int? PlayersOnline);
