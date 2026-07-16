using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Docker.Services;

public sealed class DockerContainerService : IContainerService
{
    private readonly IOptions<DockerOptions> _options;

    public DockerContainerService(IOptions<DockerOptions> options)
    {
        _options = options;
    }

    public async Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var containers = await client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);

            return containers.Select(DockerContainerMapper.Map).ToArray();
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
