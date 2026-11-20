using GamesHud.Api.GameServers.Ports;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.GameServers.Controllers;

[ApiController]
[Route("api/system/ports")]
public sealed class SystemPortsController : ControllerBase
{
    private readonly IPortAvailabilityService _portAvailabilityService;

    public SystemPortsController(IPortAvailabilityService portAvailabilityService)
    {
        _portAvailabilityService = portAvailabilityService;
    }

    [HttpGet("{protocol}/{port:int}")]
    public async Task<IActionResult> GetPortAvailability(
        string protocol,
        int port,
        CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535)
        {
            return BadRequest(new { code = PortErrorCodes.InvalidPort });
        }

        var normalizedProtocol = protocol.Trim().ToLowerInvariant();

        if (!PortProtocols.IsSupported(normalizedProtocol))
        {
            return BadRequest(new { code = PortErrorCodes.UnsupportedProtocol });
        }

        var availability = await _portAvailabilityService.CheckAvailabilityAsync(
            new NetworkPort(port, normalizedProtocol),
            cancellationToken);

        return Ok(GamePortContractMapper.Map(availability));
    }
}
