using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Runtime;

public interface IRuntimeSpecificationBuilder
{
    Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken);
}

public interface IRuntimeReconciliationSpecificationBuilder
{
    Task<(RuntimeMutationSpecification? Specification, GameDefinition? Definition)> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken);
}

public sealed class RuntimeSpecificationBuilder : IRuntimeSpecificationBuilder, IRuntimeReconciliationSpecificationBuilder
{
    private readonly IManagedServerStore _store;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly IGameDefinitionRegistry _definitions;

    public RuntimeSpecificationBuilder(IManagedServerStore store, IManagedStoragePathBuilder paths, IGameDefinitionRegistry definitions)
    {
        _store = store;
        _paths = paths;
        _definitions = definitions;
    }

    public async Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(context.GameServerId.ToString(), cancellationToken);
        var image = context.GameDefinition.RuntimeImages.SingleOrDefault(item => item.RuntimeType == context.ValidatedPlan.RuntimeType);
        if (server is null || server.InstallationType != ManagedInstallationTypes.Managed || image is null) return null;

        var portIds = context.ReservedResources.PortReservationIds.ToHashSet(StringComparer.Ordinal);
        var storageIds = context.ReservedResources.StorageReservationIds.ToHashSet(StringComparer.Ordinal);
        var ports = server.PortReservations.Where(item => portIds.Contains(item.Id)
            && item.GameServerId == server.Id && item.ProvisioningOperationId == context.OperationId
            && item.Status == ReservationStatuses.Reserved)
            .Select(item => new RuntimePortBinding(item.Id, item.PortDefinitionId, item.Protocol, item.Port, item.Exposure)).ToArray();

        var layout = _paths.CreateLayout(context.GameServerId);
        var mounts = server.StorageReservations.Where(item => storageIds.Contains(item.Id)
            && item.GameServerId == server.Id && item.ProvisioningOperationId == context.OperationId
            && item.Status == ReservationStatuses.Reserved && item.Ownership == StorageOwnerships.Managed)
            .Select(item =>
            {
                var definition = context.GameDefinition.Storages.SingleOrDefault(storage => storage.Id == item.StorageDefinitionId);
                return definition?.RuntimeTarget is null ? null : new RuntimeStorageMount(
                    item.Id, item.StorageDefinitionId,
                    ManagedStoragePathBuilder.EnsureContained(layout.DataRoot, Path.Combine(layout.DataRoot, item.RelativePath), "Runtime storage escaped the managed data root."),
                    definition.RuntimeTarget, false);
            }).Where(item => item is not null).Cast<RuntimeStorageMount>().ToArray();

        if (ports.Length != context.ReservedResources.PortReservationIds.Count
            || mounts.Length != context.ReservedResources.StorageReservationIds.Count) return null;

        var requirements = context.GameDefinition.Requirements;
        return new RuntimeMutationSpecification(context.GameServerId, context.ValidatedPlan.GameId, context.OperationId,
            context.ValidatedPlan.RuntimeType, image, ports, mounts, context.ValidatedPlan.SecretReferences,
            new RuntimeResourceLimits(requirements?.MinimumLogicalProcessors ?? 1, requirements?.Memory?.MinimumBytes ?? 1),
            RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged);
    }

    public async Task<(RuntimeMutationSpecification? Specification, GameDefinition? Definition)> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(gameServerId.ToString(), cancellationToken);
        if (server is null || server.InstallationType != ManagedInstallationTypes.Managed
            || !_definitions.TryGet(new GameId(server.GameId), out var definition)) return (null, null);
        var image = definition!.RuntimeImages.SingleOrDefault(item => item.RuntimeType == server.RuntimeType);
        if (image is null) return (null, definition);
        var ports = server.PortReservations.Where(item => item.ProvisioningOperationId == operationId
                && item.GameServerId == server.Id && item.Status == ReservationStatuses.Reserved)
            .Select(item => new RuntimePortBinding(item.Id, item.PortDefinitionId, item.Protocol, item.Port, item.Exposure)).ToArray();
        var layout = _paths.CreateLayout(gameServerId);
        var mounts = server.StorageReservations.Where(item => item.ProvisioningOperationId == operationId
                && item.GameServerId == server.Id && item.Status == ReservationStatuses.Reserved
                && item.Ownership == StorageOwnerships.Managed)
            .Select(item =>
            {
                var storage = definition.Storages.SingleOrDefault(candidate => candidate.Id == item.StorageDefinitionId);
                return storage?.RuntimeTarget is null ? null : new RuntimeStorageMount(item.Id, item.StorageDefinitionId,
                    ManagedStoragePathBuilder.EnsureContained(layout.DataRoot, Path.Combine(layout.DataRoot, item.RelativePath),
                        "Runtime storage escaped the managed data root."), storage.RuntimeTarget, false);
            }).Where(item => item is not null).Cast<RuntimeStorageMount>().ToArray();
        if (ports.Length == 0 || mounts.Length == 0) return (null, definition);
        var requirements = definition.Requirements;
        return (new RuntimeMutationSpecification(gameServerId, definition.GameId, operationId, server.RuntimeType, image,
            ports, mounts, [], new(requirements?.MinimumLogicalProcessors ?? 1, requirements?.Memory?.MinimumBytes ?? 1),
            RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged), definition);
    }
}
