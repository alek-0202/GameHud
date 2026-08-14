using GamesHud.Api.Docker.Models;
using GamesHud.Api.Palworld.Contracts;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Palworld.Controllers;

[ApiController]
[Route("api/palworld/config")]
public sealed class PalworldConfigController : ControllerBase
{
    private readonly IPalworldConfigService _palworldConfigService;
    private readonly ILogger<PalworldConfigController> _logger;

    public PalworldConfigController(
        IPalworldConfigService palworldConfigService,
        ILogger<PalworldConfigController> logger)
    {
        _palworldConfigService = palworldConfigService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _palworldConfigService.GetConfigAsync(cancellationToken);

            return Ok(config);
        }
        catch (PalworldConfigNotFoundException exception)
        {
            _logger.LogWarning(exception, "Palworld configuration file was not found.");

            return Problem(
                title: "Palworld configuration was not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (PalworldConfigException exception)
        {
            _logger.LogWarning(exception, "Palworld configuration is unavailable.");

            return Problem(
                title: "Palworld integration is not configured",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while reading Palworld configuration.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] PalworldConfigUpdateRequest request,
        [FromQuery] bool restart,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _palworldConfigService.UpdateConfigAsync(
                request,
                restart,
                cancellationToken);

            return Ok(result);
        }
        catch (PalworldConfigValidationException exception)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["palworldConfig"] = exception.Errors.ToArray()
                }));
        }
        catch (PalworldConfigNotFoundException exception)
        {
            _logger.LogWarning(exception, "Palworld configuration file was not found while saving.");

            return Problem(
                title: "Palworld configuration was not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable during Palworld lifecycle action.");

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not reach Docker Engine to control the configured Palworld container.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PalworldContainerLifecycleException exception)
        {
            _logger.LogWarning(exception, "Configured Palworld container lifecycle action failed.");

            return Problem(
                title: "Palworld container action failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (PalworldConfigWriteException exception)
        {
            _logger.LogError(exception, "Palworld configuration write failed.");

            return Problem(
                title: "Palworld configuration could not be saved",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (PalworldConfigException exception)
        {
            _logger.LogWarning(exception, "Palworld configuration is unavailable while saving.");

            return Problem(
                title: "Palworld integration is not configured",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while saving Palworld configuration.");

            return UnexpectedErrorProblem();
        }
    }

    private ObjectResult UnexpectedErrorProblem()
    {
        return Problem(
            title: "Unexpected API error",
            detail: "The API could not complete the Palworld request.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
