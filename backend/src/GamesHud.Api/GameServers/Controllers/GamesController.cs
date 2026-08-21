using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.GameServers.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController : ControllerBase
{
    private readonly IGameDefinitionRegistry _registry;

    public GamesController(IGameDefinitionRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult GetGames()
    {
        return Ok(new GameCatalogResponse(
            _registry.GetAll().Select(Map).ToArray()));
    }

    [HttpGet("{gameId}")]
    public IActionResult GetGame(string gameId)
    {
        if (!TryCreateGameId(gameId, out var id))
        {
            return NotFound();
        }

        if (!_registry.TryGet(id, out var definition))
        {
            return NotFound();
        }

        return Ok(Map(definition!));
    }

    private static GameCatalogItemResponse Map(GameDefinition definition)
    {
        return new GameCatalogItemResponse(
            definition.GameId.ToString(),
            definition.DisplayName,
            definition.Description,
            new GameCatalogBrandingResponse(
                definition.Branding.IconKey,
                definition.Branding.ImageReference),
            definition.SupportedRuntimes,
            definition.Capabilities);
    }

    private static bool TryCreateGameId(string value, out GameId gameId)
    {
        try
        {
            gameId = new GameId(value);
            return true;
        }
        catch (ArgumentException)
        {
            gameId = default;
            return false;
        }
    }
}
