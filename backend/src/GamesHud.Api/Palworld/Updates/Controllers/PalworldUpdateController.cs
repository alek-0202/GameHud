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
        catch (PalworldUpdateNotConfiguredException exception)
        {
            return ProblemWithCode(
                "Palworld update is not configured",
                exception.Message,
                StatusCodes.Status409Conflict,
                "update_not_configured");
        }
        catch (PalworldUpdateConflictException exception)
        {
            return ProblemWithCode(
                "Palworld update is already running",
                exception.Message,
                StatusCodes.Status409Conflict,
                "duplicate_update");
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable during Palworld update.");

            return ProblemWithCode(
                "Docker Engine is unavailable",
                "The API could not reach Docker Engine to manage the configured Palworld container.",
                StatusCodes.Status503ServiceUnavailable,
                "docker_unavailable");
        }
        catch (PalworldUpdateConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld update integration is not configured.");

            return UpdateUnavailableProblem(exception.Message);
        }
        catch (PalworldUpdateFailedException exception)
        {
            _logger.LogError(exception, "Palworld update failed at {Step}.", exception.FailedStep);

            return ProblemWithCode(
                "Palworld update failed",
                exception.Message,
                StatusCodes.Status409Conflict,
                MapFailedStepToErrorCode(exception.FailedStep));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while applying Palworld update.");

            return UnexpectedErrorProblem();
        }
    }

    private ObjectResult UpdateUnavailableProblem(string detail)
    {
        return ProblemWithCode(
            "Palworld update integration is not configured",
            detail,
            StatusCodes.Status503ServiceUnavailable,
            "update_unavailable");
    }

    private ObjectResult BadRequestProblem(string detail)
    {
        return ProblemWithCode(
            "Invalid Palworld update request",
            detail,
            StatusCodes.Status400BadRequest,
            "invalid_update_request");
    }

    private ObjectResult UnexpectedErrorProblem()
    {
        return ProblemWithCode(
            "Unexpected API error",
            "The API could not complete the Palworld update request.",
            StatusCodes.Status500InternalServerError,
            "unexpected_update_error");
    }

    private ObjectResult ProblemWithCode(
        string title,
        string detail,
        int statusCode,
        string errorCode)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode
        };
        problem.Extensions["errorCode"] = errorCode;

        return StatusCode(statusCode, problem);
    }

    private static string MapFailedStepToErrorCode(string failedStep)
    {
        return failedStep switch
        {
            PalworldUpdateSteps.Save => "save_failed",
            PalworldUpdateSteps.Backup => "backup_failed",
            PalworldUpdateSteps.Stop => "stop_failed",
            PalworldUpdateSteps.Update => "update_failed",
            PalworldUpdateSteps.Start => "start_failed",
            PalworldUpdateSteps.Health => "verification_failed",
            PalworldUpdateSteps.VersionCheck => "version_check_failed",
            _ => "update_failed"
        };
    }
}
