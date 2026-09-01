using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.GameServers.Runtime;

public interface IRuntimeSpecificationBuilder
{
    Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken);
}

public sealed class RuntimeSpecificationBuilder : IRuntimeSpecificationBuilder
{
    private readonly IManagedServerStore _store;
    private readonly IManagedStoragePathBuilder _paths;

    public RuntimeSpecificationBuilder(IManagedServerStore store, IManagedStoragePathBuilder paths)
    {
        _store = store;
        _paths = paths;
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
}
