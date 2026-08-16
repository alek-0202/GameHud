using GamesHud.Api.Docker.Models;
using GamesHud.Api.Palworld.Updates.Contracts;
using GamesHud.Api.Palworld.Updates.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Palworld.Updates.Controllers;

[ApiController]
[Route("api/palworld/update")]
public sealed class PalworldUpdateController : ControllerBase
{
    private readonly IPalworldUpdateService _updateService;
    private readonly ILogger<PalworldUpdateController> _logger;

    public PalworldUpdateController(
        IPalworldUpdateService updateService,
        ILogger<PalworldUpdateController> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> CheckForUpdates(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _updateService.CheckForUpdatesAsync(cancellationToken);

            return Ok(PalworldUpdateContractMapper.Map(status));
        }
        catch (PalworldUpdateConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld update integration is not configured.");

            return UpdateUnavailableProblem(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while checking Palworld updates.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost]
    public async Task<IActionResult> ApplyUpdate(
        [FromBody] PalworldUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequestProblem("Update confirmation is required.");
        }

        try
        {
            var result = await _updateService.ApplyUpdateAsync(
                request.ConfirmationText,
                cancellationToken);

            return Ok(PalworldUpdateContractMapper.Map(result));
        }
        catch (PalworldUpdateValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable during Palworld update.");

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not reach Docker Engine to manage the configured Palworld container.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PalworldUpdateConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld update integration is not configured.");

            return UpdateUnavailableProblem(exception.Message);
        }
        catch (PalworldUpdateFailedException exception)
        {
            _logger.LogError(exception, "Palworld update failed at {Step}.", exception.FailedStep);

            return Problem(
                title: "Palworld update failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while applying Palworld update.");

            return UnexpectedErrorProblem();
        }
    }

    private ObjectResult UpdateUnavailableProblem(string detail)
    {
        return Problem(
            title: "Palworld update integration is not configured",
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private ObjectResult BadRequestProblem(string detail)
    {
        return Problem(
            title: "Invalid Palworld update request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private ObjectResult UnexpectedErrorProblem()
    {
        return Problem(
            title: "Unexpected API error",
            detail: "The API could not complete the Palworld update request.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
