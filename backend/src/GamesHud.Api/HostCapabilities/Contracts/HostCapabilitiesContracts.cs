namespace GamesHud.Api.HostCapabilities.Contracts;

public sealed record HostCapabilitiesResponse(
    HostOperatingSystemResponse OperatingSystem,
    HostCpuResponse Cpu,
    HostMemoryResponse Memory,
    HostStorageResponse Storage,
    HostNetworkResponse Network,
    IReadOnlyCollection<HostRuntimeResponse> Runtimes,
    HostReadinessResponse OverallReadiness,
    IReadOnlyCollection<HostCapabilityIssueResponse> Issues);

public sealed record HostOperatingSystemResponse(
    string Family,
    string Description,
    string Architecture);

public sealed record HostCpuResponse(
    int LogicalProcessors,
    string Architecture);

public sealed record HostMemoryResponse(
    string Status,
    ulong? TotalBytes,
    ulong? AvailableBytes);

public sealed record HostStorageResponse(
    string Status,
    string ObservedRoot,
    ulong? TotalBytes,
    ulong? AvailableBytes);

public sealed record HostNetworkResponse(
    string Status,
    int InterfaceCount,
    bool LoopbackAvailable,
    bool Ipv4Available,
    bool CanInspectInterfaces);

public sealed record HostRuntimeResponse(
    string Id,
    string DisplayName,
    string Status,
    bool EndpointConfigured,
    bool Reachable,
    string? Version,
    string? OperatingSystem,
    IReadOnlyCollection<HostCapabilityIssueResponse> Issues);

public sealed record HostReadinessResponse(
    string Status,
    string Message);

public sealed record HostCapabilityIssueResponse(
    string Code,
    string Severity,
    string Message);
