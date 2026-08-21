using GamesHud.Api.HostCapabilities.Contracts;
using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.HostCapabilities.Services;

public static class HostCapabilitiesContractMapper
{
    public static HostCapabilitiesResponse Map(HostCapabilitySnapshot capabilities)
    {
        return new HostCapabilitiesResponse(
            new HostOperatingSystemResponse(
                capabilities.OperatingSystem.Family,
                capabilities.OperatingSystem.Description,
                capabilities.OperatingSystem.Architecture),
            new HostCpuResponse(
                capabilities.Cpu.LogicalProcessors,
                capabilities.Cpu.Architecture),
            new HostMemoryResponse(
                capabilities.Memory.Status,
                capabilities.Memory.TotalBytes,
                capabilities.Memory.AvailableBytes),
            new HostStorageResponse(
                capabilities.Storage.Status,
                capabilities.Storage.ObservedRoot,
                capabilities.Storage.TotalBytes,
                capabilities.Storage.AvailableBytes),
            new HostNetworkResponse(
                capabilities.Network.Status,
                capabilities.Network.InterfaceCount,
                capabilities.Network.LoopbackAvailable,
                capabilities.Network.Ipv4Available,
                capabilities.Network.CanInspectInterfaces),
            capabilities.Runtimes.Select(Map).ToArray(),
            new HostReadinessResponse(
                capabilities.OverallReadiness.Status,
                capabilities.OverallReadiness.Message),
            capabilities.Issues.Select(Map).ToArray());
    }

    private static HostRuntimeResponse Map(HostRuntimeInfo runtime)
    {
        return new HostRuntimeResponse(
            runtime.Id,
            runtime.DisplayName,
            runtime.Status,
            runtime.EndpointConfigured,
            runtime.Reachable,
            runtime.Version,
            runtime.OperatingSystem,
            runtime.Issues.Select(Map).ToArray());
    }

    private static HostCapabilityIssueResponse Map(HostCapabilityIssue issue)
    {
        return new HostCapabilityIssueResponse(
            issue.Code,
            issue.Severity,
            issue.Message);
    }
}
