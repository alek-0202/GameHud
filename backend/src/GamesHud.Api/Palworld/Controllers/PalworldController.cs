using GamesHud.Api.Docker.Models;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Palworld.Controllers;

[ApiController]
[Route("api/palworld")]
public sealed class PalworldController : ControllerBase
{
    private readonly IPalworldOverviewService _palworldOverviewService;
    private readonly ILogger<PalworldController> _logger;

    public PalworldController(
        IPalworldOverviewService palworldOverviewService,
        ILogger<PalworldController> logger)
    {
        _palworldOverviewService = palworldOverviewService;
        _logger = logger;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        try
        {
            var overview = await _palworldOverviewService.GetOverviewAsync(cancellationToken);

            return Ok(overview);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable while building Palworld overview.");

            return DockerUnavailableProblem();
        }
        catch (PalworldConfigException exception)
        {
            _logger.LogWarning(exception, "Palworld overview is not configured.");

            return Problem(
                title: "Palworld integration is not configured",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while building Palworld overview.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpGet("players")]
    public async Task<IActionResult> GetPlayers(CancellationToken cancellationToken)
    {
        try
        {
            var players = await _palworldOverviewService.GetPlayersAsync(cancellationToken);

            return Ok(players);
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Palworld REST API is unavailable while reading players.");

            return Problem(
                title: "Palworld REST API is unavailable",
                detail: GetRestErrorMessage(exception),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while reading Palworld players.");

            return UnexpectedErrorProblem();
        }
    }

    private static string GetRestErrorMessage(PalworldRestException exception)
    {
        return exception switch
        {
            PalworldRestConfigurationException => "Palworld REST API is not configured in GamesHud.",
            PalworldRestUnauthorizedException => "Palworld REST API rejected the configured credentials.",
            PalworldRestMalformedResponseException => "Palworld REST API returned a malformed response.",
            _ => "Palworld REST API is unavailable."
        };
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
            detail: "The API could not complete the Palworld request.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
