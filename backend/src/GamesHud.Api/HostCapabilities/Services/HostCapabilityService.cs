using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.HostCapabilities.Services;

public sealed class HostCapabilityService : IHostCapabilityService
{
    private readonly IHostSystemInspector _systemInspector;
    private readonly IHostResourceInspector _resourceInspector;
    private readonly IHostNetworkInspector _networkInspector;
    private readonly IHostRuntimeInspector _runtimeInspector;

    public HostCapabilityService(
        IHostSystemInspector systemInspector,
        IHostResourceInspector resourceInspector,
        IHostNetworkInspector networkInspector,
        IHostRuntimeInspector runtimeInspector)
    {
        _systemInspector = systemInspector;
        _resourceInspector = resourceInspector;
        _networkInspector = networkInspector;
        _runtimeInspector = runtimeInspector;
    }

    public async Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var system = _systemInspector.GetSystemInfo();
        var resources = await _resourceInspector.GetResourcesAsync(cancellationToken);
        var network = _networkInspector.GetNetworkInfo();
        var runtimes = await _runtimeInspector.GetRuntimesAsync(cancellationToken);
        var issues = resources.Issues
            .Concat(runtimes.SelectMany(runtime => runtime.Issues))
            .ToArray();

        return new HostCapabilitySnapshot(
            system.OperatingSystem,
            system.Cpu,
            resources.Memory,
            resources.Storage,
            network,
            runtimes,
            CalculateReadiness(resources, runtimes, issues),
            issues);
    }

    private static HostReadinessInfo CalculateReadiness(
        HostResourceInspection resources,
        IReadOnlyCollection<HostRuntimeInfo> runtimes,
        IReadOnlyCollection<HostCapabilityIssue> issues)
    {
        var docker = runtimes.FirstOrDefault(runtime => runtime.Id == "docker");

        if (docker?.Status == HostCapabilityStatuses.Available
            && resources.Storage.Status == HostCapabilityStatuses.Available
            && !issues.Any(issue => issue.Severity == HostCapabilityIssueSeverities.Blocking))
        {
            return new HostReadinessInfo(
                HostReadinessStatuses.Ready,
                "Ready to host supported game servers.");
        }

        if (docker?.Status is HostCapabilityStatuses.NotConfigured or HostCapabilityStatuses.Unavailable)
        {
            return new HostReadinessInfo(
                HostReadinessStatuses.Partial,
                "Host inspection completed, but the Docker runtime is not ready.");
        }

        return new HostReadinessInfo(
            HostReadinessStatuses.Partial,
            "Host inspection completed with limited capability information.");
    }
}
