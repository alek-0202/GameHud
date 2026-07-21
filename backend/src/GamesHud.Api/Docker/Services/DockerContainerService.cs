using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text;

namespace GamesHud.Api.Docker.Services;

public sealed class DockerContainerService : IContainerService
{
    private const string StartAction = "start";
    private const string StopAction = "stop";
    private const string RestartAction = "restart";

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

    public async Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);

            return DockerContainerMapper.MapDetails(container);
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    public async Task<ContainerLogsResponse?> GetContainerLogsAsync(
        string containerId,
        int tail,
        bool timestamps,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Tail = tail.ToString(CultureInfo.InvariantCulture),
                Timestamps = timestamps
            };

            using var stream = await client.Containers.GetContainerLogsAsync(
                containerId,
                container.Config?.Tty ?? false,
                parameters,
                cancellationToken);
            var lines = await ReadLogLinesAsync(stream, cancellationToken);

            return new ContainerLogsResponse(
                container.ID ?? containerId,
                lines,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    public async Task<ContainerLifecycleActionResponse?> StartContainerAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var previousState = GetContainerState(container);

            if (IsRunning(container))
            {
                return CreateLifecycleResponse(
                    container.ID ?? containerId,
                    StartAction,
                    true,
                    "Container is already running. No change was necessary.",
                    previousState,
                    previousState);
            }

            await client.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                cancellationToken);

            var updatedContainer = await client.Containers.InspectContainerAsync(containerId, cancellationToken);

            return CreateLifecycleResponse(
                updatedContainer.ID ?? container.ID ?? containerId,
                StartAction,
                true,
                "Container started.",
                previousState,
                GetContainerState(updatedContainer));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerConflict(exception))
        {
            return await CreateConflictResponseAsync(containerId, StartAction, cancellationToken);
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    public async Task<ContainerLifecycleActionResponse?> StopContainerAsync(
        string containerId,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var previousState = GetContainerState(container);

            if (!IsRunning(container))
            {
                return CreateLifecycleResponse(
                    container.ID ?? containerId,
                    StopAction,
                    true,
                    "Container is already stopped. No change was necessary.",
                    previousState,
                    previousState);
            }

            await client.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = (uint)timeoutSeconds
                },
                cancellationToken);

            var updatedContainer = await client.Containers.InspectContainerAsync(containerId, cancellationToken);

            return CreateLifecycleResponse(
                updatedContainer.ID ?? container.ID ?? containerId,
                StopAction,
                true,
                "Container stopped.",
                previousState,
                GetContainerState(updatedContainer));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerConflict(exception))
        {
            return await CreateConflictResponseAsync(containerId, StopAction, cancellationToken);
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    public async Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
        string containerId,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var previousState = GetContainerState(container);

            await client.Containers.RestartContainerAsync(
                containerId,
                new ContainerRestartParameters
                {
                    WaitBeforeKillSeconds = (uint)timeoutSeconds
                },
                cancellationToken);

            var updatedContainer = await client.Containers.InspectContainerAsync(containerId, cancellationToken);

            return CreateLifecycleResponse(
                updatedContainer.ID ?? container.ID ?? containerId,
                RestartAction,
                true,
                "Container restarted.",
                previousState,
                GetContainerState(updatedContainer));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (IsDockerConflict(exception))
        {
            return await CreateConflictResponseAsync(containerId, RestartAction, cancellationToken);
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

    private async Task<ContainerLifecycleActionResponse> CreateConflictResponseAsync(
        string containerId,
        string action,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
        var currentState = GetContainerState(container);

        return CreateLifecycleResponse(
            container.ID ?? containerId,
            action,
            false,
            "Docker could not complete the requested action because the container state changed or conflicts with the action.",
            currentState,
            currentState);
    }

    private static ContainerLifecycleActionResponse CreateLifecycleResponse(
        string containerId,
        string action,
        bool success,
        string message,
        string previousState,
        string currentState)
    {
        return new ContainerLifecycleActionResponse(
            containerId,
            action,
            success,
            message,
            previousState,
            currentState,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string GetContainerState(ContainerInspectResponse container)
    {
        return container.State?.Status ?? string.Empty;
    }

    private static bool IsRunning(ContainerInspectResponse container)
    {
        return container.State?.Running == true
            || string.Equals(container.State?.Status, "running", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyCollection<string>> ReadLogLinesAsync(
        MultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var result = await stream.ReadOutputAsync(
                buffer,
                0,
                buffer.Length,
                cancellationToken);

            if (result.EOF)
            {
                break;
            }

            if (result.Count > 0)
            {
                output.Write(buffer, 0, result.Count);
            }
        }

        if (output.Length == 0)
        {
            return Array.Empty<string>();
        }

        var text = Encoding.UTF8.GetString(output.ToArray());

        return SplitLogLines(text);
    }

    private static IReadOnlyCollection<string> SplitLogLines(string text)
    {
        var normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (normalizedText.Length == 0)
        {
            return Array.Empty<string>();
        }

        var lines = normalizedText.Split('\n').ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool IsContainerNotFound(Exception exception)
    {
        return exception is DockerContainerNotFoundException
            || exception is DockerApiException { StatusCode: HttpStatusCode.NotFound };
    }

    private static bool IsDockerConflict(Exception exception)
    {
        return exception is DockerApiException { StatusCode: HttpStatusCode.Conflict };
    }

    private static bool IsDockerAccessFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (IsContainerNotFound(exception) || IsDockerConflict(exception))
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
