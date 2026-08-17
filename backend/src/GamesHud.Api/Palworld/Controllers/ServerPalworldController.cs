using GamesHud.Api.Docker.Models;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Contracts;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Palworld.Controllers;

[ApiController]
[Route("api/servers/{serverId}")]
public sealed class ServerPalworldController : ControllerBase
{
    private readonly IPalworldOverviewService _overviewService;
    private readonly IPalworldConfigService _configService;
    private readonly IPalworldAdminService _adminService;
    private readonly IPalworldModsService _modsService;
    private readonly ILogger<ServerPalworldController> _logger;

    public ServerPalworldController(
        IPalworldOverviewService overviewService,
        IPalworldConfigService configService,
        IPalworldAdminService adminService,
        IPalworldModsService modsService,
        ILogger<ServerPalworldController> logger)
    {
        _overviewService = overviewService;
        _configService = configService;
        _adminService = adminService;
        _modsService = modsService;
        _logger = logger;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        string serverId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _overviewService.GetOverviewAsync(serverId, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("players")]
    public async Task<IActionResult> GetPlayers(
        string serverId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _overviewService.GetPlayersAsync(serverId, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("announcements")]
    public async Task<IActionResult> Announce(
        string serverId,
        [FromBody] PalworldAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.AnnounceAsync(serverId, request, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("players/{userId}/kick")]
    public async Task<IActionResult> Kick(
        string serverId,
        string userId,
        [FromBody] PalworldPlayerActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.KickAsync(serverId, userId, request, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("players/{userId}/ban")]
    public async Task<IActionResult> Ban(
        string serverId,
        string userId,
        [FromBody] PalworldPlayerActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.BanAsync(serverId, userId, request, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("players/unban")]
    public async Task<IActionResult> Unban(
        string serverId,
        [FromBody] PalworldUnbanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.UnbanAsync(serverId, request, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(
        string serverId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _configService.GetConfigAsync(serverId, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        string serverId,
        [FromBody] PalworldConfigUpdateRequest request,
        [FromQuery] bool restart,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _configService.UpdateConfigAsync(serverId, request, restart, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("mods")]
    public async Task<IActionResult> GetMods(
        string serverId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _modsService.GetModsAsync(serverId, cancellationToken));
        }
        catch (Exception exception) when (TryMapException(exception, out var result))
        {
            return result;
        }
    }

    private bool TryMapException(Exception exception, out IActionResult result)
    {
        switch (exception)
        {
            case OperationCanceledException:
                throw exception;
            case GameServerNotFoundException:
                result = NotFound();
                return true;
            case PalworldAdminValidationException validationException:
                result = BadRequest(new ValidationProblemDetails(
                    new Dictionary<string, string[]>
                    {
                        ["palworldAdmin"] = validationException.Errors.ToArray()
                    }));
                return true;
            case PalworldConfigValidationException validationException:
                result = BadRequest(new ValidationProblemDetails(
                    new Dictionary<string, string[]>
                    {
                        ["palworldConfig"] = validationException.Errors.ToArray()
                    }));
                return true;
            case PalworldConfigNotFoundException:
                result = Problem(
                    title: "Palworld configuration was not found",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status404NotFound);
                return true;
            case DockerUnavailableException:
                result = Problem(
                    title: "Docker Engine is unavailable",
                    detail: "The API could not reach Docker Engine.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
                return true;
            case PalworldRestException:
                result = Problem(
                    title: "Palworld REST API is unavailable",
                    detail: GetRestErrorMessage((PalworldRestException)exception),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
                return true;
            case PalworldContainerLifecycleException:
                result = Problem(
                    title: "Palworld container action failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status409Conflict);
                return true;
            case PalworldConfigException:
                result = Problem(
                    title: "Palworld integration is not configured",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
                return true;
            default:
                _logger.LogError(exception, "Unexpected Palworld server route error.");
                result = Problem(
                    title: "Unexpected API error",
                    detail: "The API could not complete the Palworld request.",
                    statusCode: StatusCodes.Status500InternalServerError);
                return true;
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
}
