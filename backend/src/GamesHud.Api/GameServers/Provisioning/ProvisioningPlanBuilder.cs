using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.GameServers.Configuration;
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
    private readonly IReadOnlyCollection<IGameProvisioningConfigurationCodec> _configurationCodecs;

    public ProvisioningPlanBuilder(
        IGameDefinitionRegistry definitions,
        IHostCapabilityService hostCapabilities,
        IGameRequirementEvaluator requirements,
        IPortPlanner portPlanner,
        IGameStoragePlanner storagePlanner,
        IEnumerable<IGameProvisioningConfigurationCodec>? configurationCodecs = null)
    {
        _definitions = definitions;
        _hostCapabilities = hostCapabilities;
        _requirements = requirements;
        _portPlanner = portPlanner;
        _storagePlanner = storagePlanner;
        _configurationCodecs = (configurationCodecs ?? []).ToArray();
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

        var minecraftOptions = request.Minecraft;
        if (gameId.Value == "minecraft")
        {
            if (minecraftOptions is null
                || !MinecraftProvisioningConfigurationCodec.AllowedJavaMemoryMiB.Contains(minecraftOptions.JavaMemoryMiB)
                || minecraftOptions.HostPort is < 1024 or > 65535
                || !minecraftOptions.EulaAccepted)
                return Failed(ProvisioningErrorCodes.InvalidRequest, "Minecraft RAM, port and explicit EULA acceptance are required.");
        }
        else if (minecraftOptions is not null)
        {
            return Failed(ProvisioningErrorCodes.InvalidRequest, "Minecraft installation choices are not valid for this game.");
        }

        var capabilities = await _hostCapabilities.GetCapabilitiesAsync(cancellationToken);
        var compatibility = _requirements.Evaluate(definition!, capabilities);
        if (compatibility.Status is GameCompatibilityStatuses.Incompatible or GameCompatibilityStatuses.Unknown)
        {
            return Failed(ProvisioningErrorCodes.HostIncompatible, "The host does not satisfy the game requirements.");
        }

        var preferredPorts = minecraftOptions is null
            ? new Dictionary<string, int>()
            : new Dictionary<string, int> { ["game"] = minecraftOptions.HostPort };
        var portPlan = await _portPlanner.CreatePlanAsync(definition!, preferredPorts, cancellationToken);
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
                item.Allocation.RequestedPort.Protocol,
                definition!.Ports.Single(port => port.Id == item.DefinitionId).DefaultPort.Number,
                item.Exposure == PortExposures.Public ? item.Allocation.AllocatedPort!.Number : null,
                item.Exposure == PortExposures.Public,
                item.Exposure))
            .ToArray();
        var storage = storagePlan.Entries
            .Select(item => new ValidatedProvisioningStorage(
                item.DefinitionId,
                item.RelativePath.Replace('\\', '/'),
                item.ApiPath,
                item.HostPath))
            .ToArray();

        var codec = _configurationCodecs.SingleOrDefault(candidate => candidate.GameId == gameId);
        var configuration = codec switch
        {
            MinecraftProvisioningConfigurationCodec minecraftCodec when minecraftOptions is not null =>
                minecraftCodec.CreateInitial(request.DisplayName.Trim(), minecraftOptions.JavaMemoryMiB,
                    minecraftOptions.HostPort, minecraftOptions.EulaAccepted),
            not null => codec.CreateInitial(request.DisplayName.Trim()),
            _ => null
        };
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
                ProvisioningStepIds.All,
                configuration),
            definition,
            null);
    }

    private static ProvisioningPlanBuildResult Failed(string code, string message) =>
        new(null, null, new ProvisioningFailure(code, message));
}
