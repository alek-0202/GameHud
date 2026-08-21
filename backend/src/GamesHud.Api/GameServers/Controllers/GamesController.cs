using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.HostCapabilities.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.GameServers.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController : ControllerBase
{
    private readonly IGameDefinitionRegistry _registry;
    private readonly IHostCapabilityService _hostCapabilityService;
    private readonly IGameRequirementEvaluator _requirementEvaluator;
    private readonly IPortPlanner _portPlanner;

    public GamesController(
        IGameDefinitionRegistry registry,
        IHostCapabilityService hostCapabilityService,
        IGameRequirementEvaluator requirementEvaluator,
        IPortPlanner portPlanner)
    {
        _registry = registry;
        _hostCapabilityService = hostCapabilityService;
        _requirementEvaluator = requirementEvaluator;
        _portPlanner = portPlanner;
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

    [HttpGet("{gameId}/compatibility")]
    public async Task<IActionResult> GetGameCompatibility(
        string gameId,
        CancellationToken cancellationToken)
    {
        if (!TryCreateGameId(gameId, out var id))
        {
            return NotFound();
        }

        if (!_registry.TryGet(id, out var definition))
        {
            return NotFound();
        }

        var hostCapabilities = await _hostCapabilityService.GetCapabilitiesAsync(cancellationToken);
        var assessment = _requirementEvaluator.Evaluate(definition!, hostCapabilities);

        return Ok(GameCompatibilityContractMapper.Map(assessment));
    }

    [HttpPost("{gameId}/ports/plan")]
    public async Task<IActionResult> PlanGamePorts(
        string gameId,
        CancellationToken cancellationToken)
    {
        if (!TryCreateGameId(gameId, out var id))
        {
            return NotFound();
        }

        if (!_registry.TryGet(id, out var definition))
        {
            return NotFound();
        }

        var plan = await _portPlanner.CreatePlanAsync(definition!, cancellationToken);

        return Ok(GamePortContractMapper.Map(plan));
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
