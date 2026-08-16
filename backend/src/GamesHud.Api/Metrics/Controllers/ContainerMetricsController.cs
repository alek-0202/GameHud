using GamesHud.Api.Docker.Models;
using GamesHud.Api.Metrics.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Metrics.Controllers;

[ApiController]
[Route("api/containers/{containerId}/metrics")]
public sealed class ContainerMetricsController : ControllerBase
{
    private readonly IMetricsService _metricsService;
    private readonly ILogger<ContainerMetricsController> _logger;

    public ContainerMetricsController(
        IMetricsService metricsService,
        ILogger<ContainerMetricsController> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetContainerMetrics(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await _metricsService.GetContainerMetricsAsync(
                containerId,
                cancellationToken);

            if (metrics is null)
            {
                return NotFound(new
                {
                    message = "Container was not found."
                });
            }

            return Ok(MetricsContractMapper.Map(metrics));
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker Engine is unavailable while reading metrics for container {ContainerId}.",
                containerId);

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not read container metrics from Docker Engine.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected error while reading metrics for container {ContainerId}.",
                containerId);

            return Problem(
                title: "Unexpected API error",
                detail: "The API could not complete the metrics request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
