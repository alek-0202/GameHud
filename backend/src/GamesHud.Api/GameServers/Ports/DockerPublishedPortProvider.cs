using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using Microsoft.Extensions.Options;
using System.Net;

namespace GamesHud.Api.GameServers.Ports;

public sealed class DockerPublishedPortProvider : IDockerPublishedPortProvider
{
    private readonly IOptions<DockerOptions> _options;

    public DockerPublishedPortProvider(IOptions<DockerOptions> options)
    {
        _options = options;
    }

    public async Task<IReadOnlyCollection<DockerPublishedPort>> GetPublishedPortsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var containers = await client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);

            return containers
                .SelectMany(MapPublishedPorts)
                .ToArray();
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            return [];
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

    private static IEnumerable<DockerPublishedPort> MapPublishedPorts(ContainerListResponse container)
    {
        foreach (var port in container.Ports ?? [])
        {
            if (port.PublicPort is < 1)
            {
                continue;
            }

            var protocol = (port.Type ?? string.Empty).Trim().ToLowerInvariant();

            if (!PortProtocols.IsSupported(protocol))
            {
                continue;
            }

            yield return new DockerPublishedPort(
                new NetworkPort(port.PublicPort, protocol),
                container.ID ?? string.Empty,
                NormalizeContainerName(container.Names?.FirstOrDefault()));
        }
    }

    private static string NormalizeContainerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "container";
        }

        return name.Trim().TrimStart('/');
    }

    private static bool IsDockerAccessFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is DockerApiException
            or HttpRequestException
            or IOException
            or TimeoutException
            or TaskCanceledException
            or UriFormatException
            or UnauthorizedAccessException;
    }
}
