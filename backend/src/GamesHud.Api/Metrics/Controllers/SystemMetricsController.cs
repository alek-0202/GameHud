using GamesHud.Api.Docker.Models;
using GamesHud.Api.Metrics.Contracts;
using GamesHud.Api.Metrics.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Metrics.Controllers;

[ApiController]
[Route("api/system/metrics")]
public sealed class SystemMetricsController : ControllerBase
{
    private readonly IMetricsService _metricsService;
    private readonly IMetricsHistoryStore _historyStore;
    private readonly ILogger<SystemMetricsController> _logger;

    public SystemMetricsController(
        IMetricsService metricsService,
        IMetricsHistoryStore historyStore,
        ILogger<SystemMetricsController> logger)
    {
        _metricsService = metricsService;
        _historyStore = historyStore;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSystemMetrics(
        [FromQuery] int? historyHours,
        CancellationToken cancellationToken)
    {
        if (!TryResolveHistoryWindow(historyHours, out var resolvedHistoryHours, out var problem))
        {
            return problem;
        }

        try
        {
            var hostMetrics = await _metricsService.GetHostMetricsAsync(cancellationToken);
            var dockerMetrics = await _metricsService.GetDockerSummaryMetricsAsync(cancellationToken);
            var history = _historyStore
                .GetSince(DateTimeOffset.UtcNow.AddHours(-resolvedHistoryHours))
                .Select(MetricsContractMapper.Map)
                .ToArray();

            return Ok(new SystemMetricsResponse(
                MetricsContractMapper.Map(hostMetrics),
                MetricsContractMapper.Map(dockerMetrics),
                history));
        }
        catch (HostMetricsUnavailableException exception)
        {
            _logger.LogWarning(exception, "Host metrics are unavailable.");

            return Problem(
                title: "Host metrics are unavailable",
                detail: "The API could not read host metrics from the current environment.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable while reading system metrics.");

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not read Docker metrics from Docker Engine.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while reading system metrics.");

            return Problem(
                title: "Unexpected API error",
                detail: "The API could not complete the metrics request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private bool TryResolveHistoryWindow(
        int? historyHours,
        out int resolvedHistoryHours,
        out ObjectResult problem)
    {
        resolvedHistoryHours = historyHours ?? 1;

        if (resolvedHistoryHours is >= 1 and <= 24)
        {
            problem = null!;
            return true;
        }

        problem = Problem(
            title: "Invalid history window",
            detail: "The historyHours query parameter must be between 1 and 24.",
            statusCode: StatusCodes.Status400BadRequest);

        return false;
    }
}
