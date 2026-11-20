using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Updates.Services;

public sealed class DockerPalworldContainerCommandService : IPalworldContainerCommandService
{
    private const int MaxOutputLength = 16_384;

    private readonly IOptions<DockerOptions> _options;
    private readonly IOptions<PalworldOptions> _palworldOptions;

    public DockerPalworldContainerCommandService(
        IOptions<DockerOptions> options,
        IOptions<PalworldOptions> palworldOptions)
    {
        _options = options;
        _palworldOptions = palworldOptions;
    }

    public async Task<PalworldContainerCommandResult> ExecuteAsync(
        string containerName,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken)
    {
        if (command.Count == 0)
        {
            throw new PalworldUpdateCommandException("Container command cannot be empty.");
        }

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(ResolveCommandTimeoutSeconds()));
        var timeoutToken = timeoutCancellationTokenSource.Token;

        try
        {
            using var client = CreateClient();
            var exec = await client.Exec.ExecCreateContainerAsync(
                containerName,
                new ContainerExecCreateParameters
                {
                    AttachStderr = true,
                    AttachStdout = true,
                    Cmd = command.ToList()
                },
                timeoutToken);

            using var stream = await client.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                timeoutToken);
            var output = await ReadOutputAsync(stream, timeoutToken);
            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, timeoutToken);

            return new PalworldContainerCommandResult(
                (int)inspect.ExitCode,
                TruncateOutput(output));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            throw new PalworldUpdateCommandException("Configured Palworld container was not found.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PalworldUpdateCommandException("Palworld container command timed out.", exception);
        }
        catch (Exception exception) when (IsDockerAccessFailure(exception, cancellationToken))
        {
            throw new DockerUnavailableException("Docker Engine is unavailable.", exception);
        }
    }

    public async Task<string?> ReadEnvironmentVariableAsync(
        string containerName,
        string variableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(variableName)
            || variableName.Contains('=', StringComparison.Ordinal))
        {
            throw new PalworldUpdateCommandException("Environment variable name is invalid.");
        }

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(ResolveCommandTimeoutSeconds()));
        var timeoutToken = timeoutCancellationTokenSource.Token;

        try
        {
            using var client = CreateClient();
            var container = await client.Containers.InspectContainerAsync(containerName, timeoutToken);
            var prefix = $"{variableName.Trim()}=";
            var match = container.Config?.Env?
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));

            return match is null ? null : match[prefix.Length..];
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            throw new PalworldUpdateCommandException("Configured Palworld container was not found.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PalworldUpdateCommandException("Palworld container environment inspection timed out.", exception);
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

    private static async Task<string> ReadOutputAsync(
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

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string TruncateOutput(string output)
    {
        return output.Length <= MaxOutputLength
            ? output
            : output[^MaxOutputLength..];
    }

    private int ResolveCommandTimeoutSeconds()
    {
        return Math.Clamp(_palworldOptions.Value.Updates.CommandTimeoutSeconds, 5, 300);
    }

    private static bool IsContainerNotFound(Exception exception)
    {
        return exception is DockerContainerNotFoundException
            || exception is DockerApiException { StatusCode: HttpStatusCode.NotFound };
    }

    private static bool IsDockerAccessFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (IsContainerNotFound(exception))
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
