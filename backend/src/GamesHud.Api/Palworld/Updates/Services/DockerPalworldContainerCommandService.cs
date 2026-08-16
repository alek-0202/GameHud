using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Models;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Updates.Services;

public sealed class DockerPalworldContainerCommandService : IPalworldContainerCommandService
{
    private const int MaxOutputLength = 16_384;

    private readonly IOptions<DockerOptions> _options;

    public DockerPalworldContainerCommandService(IOptions<DockerOptions> options)
    {
        _options = options;
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
                cancellationToken);

            using var stream = await client.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken);
            var output = await ReadOutputAsync(stream, cancellationToken);
            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);

            return new PalworldContainerCommandResult(
                (int)inspect.ExitCode,
                TruncateOutput(output));
        }
        catch (Exception exception) when (IsContainerNotFound(exception))
        {
            throw new PalworldUpdateCommandException("Configured Palworld container was not found.");
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
