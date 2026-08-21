namespace GamesHud.Api.HostCapabilities.Models;

public static class HostCapabilityStatuses
{
    public const string Available = "available";
    public const string NotConfigured = "not_configured";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
    public const string Unsupported = "unsupported";
}

public static class HostReadinessStatuses
{
    public const string Ready = "ready";
    public const string Partial = "partial";
    public const string NotReady = "not_ready";
}

public static class HostCapabilityIssueSeverities
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
    public const string Info = "info";
}

public sealed record HostCapabilitySnapshot(
    HostOperatingSystemInfo OperatingSystem,
    HostCpuInfo Cpu,
    HostMemoryInfo Memory,
    HostStorageInfo Storage,
    HostNetworkInfo Network,
    IReadOnlyCollection<HostRuntimeInfo> Runtimes,
    HostReadinessInfo OverallReadiness,
    IReadOnlyCollection<HostCapabilityIssue> Issues);

public sealed record HostOperatingSystemInfo(
    string Family,
    string Description,
    string Architecture);

public sealed record HostCpuInfo(
    int LogicalProcessors,
    string Architecture);

public sealed record HostMemoryInfo(
    string Status,
    ulong? TotalBytes,
    ulong? AvailableBytes);

public sealed record HostStorageInfo(
    string Status,
    string ObservedRoot,
    ulong? TotalBytes,
    ulong? AvailableBytes);

public sealed record HostNetworkInfo(
    string Status,
    int InterfaceCount,
    bool LoopbackAvailable,
    bool Ipv4Available,
    bool CanInspectInterfaces);

public sealed record HostRuntimeInfo(
    string Id,
    string DisplayName,
    string Status,
    bool EndpointConfigured,
    bool Reachable,
    string? Version,
    string? OperatingSystem,
    IReadOnlyCollection<HostCapabilityIssue> Issues);

public sealed record HostReadinessInfo(
    string Status,
    string Message);

public sealed record HostCapabilityIssue(
    string Code,
    string Severity,
    string Message);

public sealed record HostResourceInspection(
    HostMemoryInfo Memory,
    HostStorageInfo Storage,
    IReadOnlyCollection<HostCapabilityIssue> Issues);

public sealed record DockerRuntimeInspection(
    bool EndpointConfigured,
    bool Reachable,
    string? Version,
    string? OperatingSystem);
