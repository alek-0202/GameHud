using GamesHud.Api.Persistence.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Persistence.Controllers;

[ApiController]
[Route("api/system/persistence")]
public sealed class PersistenceController : ControllerBase
{
    private readonly IPersistenceHealthService _healthService;

    public PersistenceController(IPersistenceHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public async Task<ActionResult<PersistenceStatusResponse>> GetPersistenceStatus(
        CancellationToken cancellationToken)
    {
        var status = await _healthService.GetStatusAsync(cancellationToken);

        return Ok(new PersistenceStatusResponse(
            status.Available,
            status.Provider,
            status.MigrationStatus,
            status.AppliedMigration,
            status.ErrorCode));
    }
}
