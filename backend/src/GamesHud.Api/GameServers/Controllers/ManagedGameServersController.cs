using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.GameServers.Controllers;

[ApiController]
[Route("api/game-servers")]
public sealed class ManagedGameServersController : ControllerBase
{
    private readonly IManagedGameServerApplicationService _application;
    private readonly IManagedGameServerQueryService _queries;
    public ManagedGameServersController(IManagedGameServerApplicationService application, IManagedGameServerQueryService queries)
    { _application = application; _queries = queries; }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManagedGameServerRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await _application.CreateAsync(request, idempotencyKey, cancellationToken);
        if (!result.Succeeded)
        {
            var error = new ManagedGameServerApiError(result.ErrorCode!, result.ErrorMessage!);
            return result.ErrorCode switch
            {
                "idempotency_conflict" => Conflict(error),
                Provisioning.ProvisioningErrorCodes.GameNotFound => NotFound(error),
                Provisioning.ProvisioningErrorCodes.HostIncompatible => UnprocessableEntity(error),
                Provisioning.ProvisioningErrorCodes.PortConflict or Provisioning.ProvisioningErrorCodes.StorageConflict
                    or Provisioning.ProvisioningErrorCodes.ReservationFailed => Conflict(error),
                _ => BadRequest(error)
            };
        }
        var response = new CreateManagedGameServerResponse(result.GameServerId!, result.OperationId!);
        return AcceptedAtAction(nameof(Get), new { id = result.GameServerId }, response);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var result = await _queries.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/provisioning")]
    public async Task<IActionResult> GetProvisioning(string id, CancellationToken cancellationToken)
    {
        var result = await _queries.GetProvisioningAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
