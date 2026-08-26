using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.HostCapabilities.Services;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IProvisioningPlanBuilder
{
    Task<ProvisioningPlanBuildResult> BuildAsync(
        CreateGameServerProvisioningRequest request,
        CancellationToken cancellationToken);
}

public sealed class ProvisioningPlanBuilder : IProvisioningPlanBuilder
{
    private readonly IGameDefinitionRegistry _definitions;
    private readonly IHostCapabilityService _hostCapabilities;
    private readonly IGameRequirementEvaluator _requirements;
    private readonly IPortPlanner _portPlanner;
    private readonly IGameStoragePlanner _storagePlanner;

    public ProvisioningPlanBuilder(
        IGameDefinitionRegistry definitions,
        IHostCapabilityService hostCapabilities,
        IGameRequirementEvaluator requirements,
        IPortPlanner portPlanner,
        IGameStoragePlanner storagePlanner)
    {
        _definitions = definitions;
        _hostCapabilities = hostCapabilities;
        _requirements = requirements;
        _portPlanner = portPlanner;
        _storagePlanner = storagePlanner;
    }

    public async Task<ProvisioningPlanBuildResult> BuildAsync(
        CreateGameServerProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.GameServerId)
            || string.IsNullOrWhiteSpace(request.GameId)
            || string.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Trim().Length > 200)
        {
            return Failed(ProvisioningErrorCodes.InvalidRequest, "Game server id, game id and display name are required.");
        }

        GameId gameId;
        GameServerId gameServerId;
        try
        {
            gameId = new GameId(request.GameId);
            gameServerId = new GameServerId(request.GameServerId);
        }
        catch (ArgumentException)
        {
            return Failed(ProvisioningErrorCodes.InvalidGameServerId, "Game server id is invalid.");
        }

        if (!_definitions.TryGet(gameId, out var definition))
        {
            return Failed(ProvisioningErrorCodes.GameNotFound, "The requested game is not registered.");
        }

        var capabilities = await _hostCapabilities.GetCapabilitiesAsync(cancellationToken);
        var compatibility = _requirements.Evaluate(definition!, capabilities);
        if (compatibility.Status is GameCompatibilityStatuses.Incompatible or GameCompatibilityStatuses.Unknown)
        {
            return Failed(ProvisioningErrorCodes.HostIncompatible, "The host does not satisfy the game requirements.");
        }

        var portPlan = await _portPlanner.CreatePlanAsync(definition!, cancellationToken);
        if (portPlan.Status is PortPlanStatuses.Conflict or PortPlanStatuses.Unknown)
        {
            return Failed(ProvisioningErrorCodes.PortConflict, "Required game ports could not be planned safely.");
        }

        GameStoragePlan storagePlan;
        try
        {
            storagePlan = _storagePlanner.CreatePlan(definition!, gameServerId);
        }
        catch (ArgumentException)
        {
            return Failed(ProvisioningErrorCodes.InvalidGameServerId, "Game server id is invalid.");
        }
        catch (StoragePlanningException)
        {
            return Failed(ProvisioningErrorCodes.StorageConflict, "Managed storage could not be planned safely.");
        }

        if (storagePlan.Status is StoragePlanStatuses.Collision or StoragePlanStatuses.Insufficient or StoragePlanStatuses.Unknown)
        {
            return Failed(ProvisioningErrorCodes.StorageConflict, "Managed storage could not be planned safely.");
        }

        var ports = portPlan.Ports
            .Where(item => item.Allocation.AllocatedPort is not null)
            .Select(item => new ValidatedProvisioningPort(
                item.DefinitionId,
                item.Allocation.AllocatedPort!.Protocol,
                item.Allocation.AllocatedPort.Number,
                item.Exposure))
            .ToArray();
        var storage = storagePlan.Entries
            .Select(item => new ValidatedProvisioningStorage(
                item.DefinitionId,
                item.RelativePath.Replace('\\', '/')))
            .ToArray();

        return new ProvisioningPlanBuildResult(
            new ValidatedProvisioningPlan(
                gameServerId,
                gameId,
                request.DisplayName.Trim(),
                definition!.SupportedRuntimes.First(),
                compatibility.Status,
                compatibility.Warnings,
                ports,
                storage,
                [],
                ProvisioningStepIds.All),
            definition,
            null);
    }

    private static ProvisioningPlanBuildResult Failed(string code, string message) =>
        new(null, null, new ProvisioningFailure(code, message));
}
