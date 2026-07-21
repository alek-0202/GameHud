using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ContainersController : ControllerBase
{
    private const int DefaultLogTail = 200;
    private const int MaximumLogTail = 2000;
    private const int DefaultLifecycleTimeoutSeconds = 10;
    private const int MinimumLifecycleTimeoutSeconds = 1;
    private const int MaximumLifecycleTimeoutSeconds = 120;

    private readonly IContainerService _containerService;
    private readonly ILogger<ContainersController> _logger;

    public ContainersController(
        IContainerService containerService,
        ILogger<ContainersController> logger)
    {
        _containerService = containerService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetContainers(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _containerService.GetContainersAsync(cancellationToken);

            return Ok(containers);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable while listing containers.");

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while listing containers.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpGet("{containerId}")]
    public async Task<IActionResult> GetContainerDetails(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var container = await _containerService.GetContainerDetailsAsync(
                containerId,
                cancellationToken);

            if (container is null)
            {
                return NotFound(new
                {
                    message = "Container was not found."
                });
            }

            return Ok(container);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while inspecting container {ContainerId}.",
                containerId);

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while inspecting container {ContainerId}.",
                containerId);

            return UnexpectedErrorProblem();
        }
    }

    [HttpGet("{containerId}/logs")]
    public async Task<IActionResult> GetContainerLogs(
        string containerId,
        [FromQuery] int? tail,
        [FromQuery] bool? timestamps,
        CancellationToken cancellationToken)
    {
        var requestedTail = tail ?? DefaultLogTail;
        var includeTimestamps = timestamps ?? true;

        if (requestedTail is < 1 or > MaximumLogTail)
        {
            return Problem(
                title: "Invalid log tail",
                detail: $"The tail query parameter must be between 1 and {MaximumLogTail}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var logs = await _containerService.GetContainerLogsAsync(
                containerId,
                requestedTail,
                includeTimestamps,
                cancellationToken);

            if (logs is null)
            {
                return NotFound(new
                {
                    message = "Container was not found."
                });
            }

            return Ok(logs);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while reading logs for container {ContainerId}.",
                containerId);

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while reading logs for container {ContainerId}.",
                containerId);

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost("{containerId}/start")]
    public async Task<IActionResult> StartContainer(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _containerService.StartContainerAsync(containerId, cancellationToken);

            return LifecycleActionResult(result);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while starting container {ContainerId}.",
                containerId);

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while starting container {ContainerId}.",
                containerId);

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost("{containerId}/stop")]
    public async Task<IActionResult> StopContainer(
        string containerId,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLifecycleTimeout(timeoutSeconds, out var requestedTimeout, out var problem))
        {
            return problem;
        }

        try
        {
            var result = await _containerService.StopContainerAsync(
                containerId,
                requestedTimeout,
                cancellationToken);

            return LifecycleActionResult(result);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while stopping container {ContainerId}.",
                containerId);

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while stopping container {ContainerId}.",
                containerId);

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost("{containerId}/restart")]
    public async Task<IActionResult> RestartContainer(
        string containerId,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLifecycleTimeout(timeoutSeconds, out var requestedTimeout, out var problem))
        {
            return problem;
        }

        try
        {
            var result = await _containerService.RestartContainerAsync(
                containerId,
                requestedTimeout,
                cancellationToken);

            return LifecycleActionResult(result);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while restarting container {ContainerId}.",
                containerId);

            return DockerUnavailableProblem();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while restarting container {ContainerId}.",
                containerId);

            return UnexpectedErrorProblem();
        }
    }

    private IActionResult LifecycleActionResult(ContainerLifecycleActionResponse? result)
    {
        if (result is null)
        {
            return NotFound(new
            {
                message = "Container was not found."
            });
        }

        if (!result.Success)
        {
            return Conflict(result);
        }

        return Ok(result);
    }

    private bool TryResolveLifecycleTimeout(
        int? timeoutSeconds,
        out int requestedTimeout,
        out ObjectResult problem)
    {
        requestedTimeout = timeoutSeconds ?? DefaultLifecycleTimeoutSeconds;

        if (requestedTimeout is >= MinimumLifecycleTimeoutSeconds and <= MaximumLifecycleTimeoutSeconds)
        {
            problem = null!;
            return true;
        }

        problem = Problem(
            title: "Invalid lifecycle timeout",
            detail: $"The timeoutSeconds query parameter must be between {MinimumLifecycleTimeoutSeconds} and {MaximumLifecycleTimeoutSeconds}.",
            statusCode: StatusCodes.Status400BadRequest);

        return false;
    }

    private ObjectResult DockerUnavailableProblem()
    {
        return Problem(
            title: "Docker Engine is unavailable",
            detail: "The API could not connect to Docker. Check whether Docker is running and the configured endpoint is accessible.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private ObjectResult UnexpectedErrorProblem()
    {
        return Problem(
            title: "Unexpected API error",
            detail: "The API could not complete the request.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
