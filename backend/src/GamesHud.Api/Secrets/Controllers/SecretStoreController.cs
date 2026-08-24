using GamesHud.Api.Secrets.Contracts;
using GamesHud.Api.Secrets.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Secrets.Controllers;

[ApiController]
[Route("api/system/secrets")]
public sealed class SecretStoreController : ControllerBase
{
    private readonly ISecretStoreHealthService _healthService;

    public SecretStoreController(ISecretStoreHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public ActionResult<SecretStoreStatusResponse> GetSecretStoreStatus()
    {
        var status = _healthService.GetStatus();

        return Ok(new SecretStoreStatusResponse(
            status.Available,
            status.Provider,
            status.Status,
            status.ErrorCode));
    }
}
