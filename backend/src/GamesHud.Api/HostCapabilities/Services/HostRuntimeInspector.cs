using Docker.DotNet;
using GamesHud.Api.Configuration;
using GamesHud.Api.HostCapabilities.Models;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.HostCapabilities.Services;

public interface IHostRuntimeInspector
{
    Task<IReadOnlyCollection<HostRuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken);
}

public interface IDockerRuntimeClient
{
    Task<DockerRuntimeInspection> InspectAsync(CancellationToken cancellationToken);
}

public sealed class HostRuntimeInspector : IHostRuntimeInspector
{
    private readonly IDockerRuntimeClient _dockerRuntimeClient;

    public HostRuntimeInspector(IDockerRuntimeClient dockerRuntimeClient)
    {
        _dockerRuntimeClient = dockerRuntimeClient;
    }

    public async Task<IReadOnlyCollection<HostRuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken)
    {
        var docker = await GetDockerRuntimeAsync(cancellationToken);

        return [docker];
    }

    private async Task<HostRuntimeInfo> GetDockerRuntimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var inspection = await _dockerRuntimeClient.InspectAsync(cancellationToken);

            if (inspection.Reachable)
            {
                return new HostRuntimeInfo(
                    "docker",
                    "Docker",
                    HostCapabilityStatuses.Available,
                    inspection.EndpointConfigured,
                    true,
                    inspection.Version,
                    inspection.OperatingSystem,
                    []);
            }

            return CreateUnavailableDockerRuntime(inspection.EndpointConfigured);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateUnavailableDockerRuntime(endpointConfigured: false);
        }
    }

    private static HostRuntimeInfo CreateUnavailableDockerRuntime(bool endpointConfigured)
    {
        var status = endpointConfigured
            ? HostCapabilityStatuses.Unavailable
            : HostCapabilityStatuses.NotConfigured;
        var code = endpointConfigured
            ? "docker_unavailable"
            : "docker_not_configured";
        var message = endpointConfigured
            ? "Docker is configured but the daemon cannot be reached."
            : "Docker is not configured and the default Docker daemon cannot be reached.";

        return new HostRuntimeInfo(
            "docker",
            "Docker",
            status,
            endpointConfigured,
            false,
            null,
            null,
            [
                new HostCapabilityIssue(
                    code,
                    HostCapabilityIssueSeverities.Blocking,
                    message)
            ]);
    }
}

public sealed class DockerRuntimeClient : IDockerRuntimeClient
{
    private readonly IOptions<DockerOptions> _options;

    public DockerRuntimeClient(IOptions<DockerOptions> options)
    {
        _options = options;
    }

    public async Task<DockerRuntimeInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var endpointConfigured = !string.IsNullOrWhiteSpace(_options.Value.Endpoint);

        try
        {
            using var client = CreateClient(endpointConfigured);
            var version = await client.System.GetVersionAsync(cancellationToken);

            return new DockerRuntimeInspection(
                endpointConfigured,
                true,
                version.Version,
                version.Os);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DockerRuntimeInspection(endpointConfigured, false, null, null);
        }
    }

    private IDockerClient CreateClient(bool endpointConfigured)
    {
        if (!endpointConfigured)
        {
            return new DockerClientConfiguration().CreateClient();
        }

        return new DockerClientConfiguration(new Uri(_options.Value.Endpoint!)).CreateClient();
    }
}
