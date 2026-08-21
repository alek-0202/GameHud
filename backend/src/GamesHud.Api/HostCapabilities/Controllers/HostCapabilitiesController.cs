using GamesHud.Api.HostCapabilities.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.HostCapabilities.Controllers;

[ApiController]
[Route("api/system/capabilities")]
public sealed class HostCapabilitiesController : ControllerBase
{
    private readonly IHostCapabilityService _hostCapabilityService;
    private readonly ILogger<HostCapabilitiesController> _logger;

    public HostCapabilitiesController(
        IHostCapabilityService hostCapabilityService,
        ILogger<HostCapabilitiesController> logger)
    {
        _hostCapabilityService = hostCapabilityService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCapabilities(CancellationToken cancellationToken)
    {
        try
        {
            var capabilities = await _hostCapabilityService.GetCapabilitiesAsync(cancellationToken);

            return Ok(HostCapabilitiesContractMapper.Map(capabilities));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while inspecting host capabilities.");

            return Problem(
                title: "Host capabilities are unavailable",
                detail: "The API could not complete host capability inspection.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
