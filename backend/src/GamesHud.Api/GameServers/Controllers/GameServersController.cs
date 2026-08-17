using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.GameServers.Controllers;

[ApiController]
[Route("api/servers")]
public sealed class GameServersController : ControllerBase
{
    private readonly IGameServerRegistry _registry;

    public GameServersController(IGameServerRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult GetServers()
    {
        return Ok(new GameServersResponse(
            _registry.GetServers().Select(Map).ToArray()));
    }

    [HttpGet("{serverId}")]
    public IActionResult GetServer(string serverId)
    {
        try
        {
            return Ok(Map(_registry.GetServer(serverId)));
        }
        catch (GameServerNotFoundException)
        {
            return NotFound();
        }
    }

    private static GameServerResponse Map(GameServerDescriptor server)
    {
        return new GameServerResponse(
            server.Id,
            server.GameType,
            server.DisplayName,
            server.ContainerName,
            server.BrandingImage,
            server.Capabilities);
    }
}
