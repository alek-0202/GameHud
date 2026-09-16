using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Persistence.Provisioning;

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
    private readonly IProvisioningOperationStore? _operations;
    private readonly IRuntimeImageIntentStore? _images;

    public RuntimeSpecificationBuilder(IManagedServerStore store, IManagedStoragePathBuilder paths,
        IGameDefinitionRegistry definitions) : this(store, paths, definitions, null, null) { }

    public RuntimeSpecificationBuilder(IManagedServerStore store, IManagedStoragePathBuilder paths, IGameDefinitionRegistry definitions,
        IProvisioningOperationStore? operations, IRuntimeImageIntentStore? images)
    {
        _store = store;
        _paths = paths;
        _definitions = definitions;
        _operations = operations;
        _images = images;
    }

    public async Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(context.GameServerId.ToString(), cancellationToken);
        var operation = _operations is null ? null : await _operations.GetAsync(context.OperationId, cancellationToken);
        VerifiedRuntimeImage? verifiedImage = null;
        var image = context.GameDefinition.LegacyRuntimeImages.SingleOrDefault(item => item.RuntimeType == context.ValidatedPlan.RuntimeType);
        if (operation?.PipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion)
        {
            if (_images is null) return null;
            try
            {
                verifiedImage = await _images.LoadVerifiedAsync(new(context.OperationId, context.GameServerId.ToString(),
                    context.ValidatedPlan.GameId.ToString(), context.ValidatedPlan.RuntimeType), cancellationToken);
                image = verifiedImage.ApprovedImage;
            }
            catch (InvalidOperationException) { return null; }
        }
        if (server is null || server.InstallationType != ManagedInstallationTypes.Managed || image is null) return null;

        var portIds = context.ReservedResources.PortReservationIds.ToHashSet(StringComparer.Ordinal);
        var storageIds = context.ReservedResources.StorageReservationIds.ToHashSet(StringComparer.Ordinal);
        var ports = server.PortReservations.Where(item => portIds.Contains(item.Id)
            && item.GameServerId == server.Id && item.ProvisioningOperationId == context.OperationId
            && item.Status == ReservationStatuses.Reserved)
            .Select(item => new RuntimePortBinding(item.Id, item.PortDefinitionId, item.Protocol,
                item.ContainerPort, item.HostPort, item.Published, item.Exposure)).ToArray();

        var mounts = server.StorageReservations.Where(item => storageIds.Contains(item.Id)
            && item.GameServerId == server.Id && item.ProvisioningOperationId == context.OperationId
            && item.Status == ReservationStatuses.Reserved && item.Ownership == StorageOwnerships.Managed)
            .Select(item =>
            {
                var definition = context.GameDefinition.Storages.SingleOrDefault(storage => storage.Id == item.StorageDefinitionId);
                if (definition?.RuntimeTarget is null) return null;
                var historical = _paths.CreateLayout(context.GameServerId);
                var apiPath = ManagedStorageTargetBuilder.ResolvePersistedPath(item.ApiPath, item.RelativePath, historical.DataRoot);
                var hostPath = ManagedStorageTargetBuilder.ResolvePersistedPath(item.HostPath, item.RelativePath, historical.HostRoot);
                return new RuntimeStorageMount(item.Id, item.StorageDefinitionId, hostPath,
                    definition.RuntimeTarget, false, apiPath,
                    ManagedStorageTargetBuilder.GetPersistedRoot(hostPath, item.RelativePath));
            }).Where(item => item is not null).Cast<RuntimeStorageMount>().ToArray();

        if (ports.Length != context.ReservedResources.PortReservationIds.Count
            || mounts.Length != context.ReservedResources.StorageReservationIds.Count) return null;

        var requirements = context.GameDefinition.Requirements;
        var environment = context.GameDefinition.RuntimeEnvironment
            .Where(item => item.RuntimeType == context.ValidatedPlan.RuntimeType).ToArray();
        return new RuntimeMutationSpecification(context.GameServerId, context.ValidatedPlan.GameId, context.OperationId,
            context.ValidatedPlan.RuntimeType, image, ports, mounts, context.ValidatedPlan.SecretReferences, environment,
            new RuntimeResourceLimits(requirements?.MinimumLogicalProcessors ?? 1, requirements?.Memory?.MinimumBytes ?? 1),
            RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged, verifiedImage);
    }

    public async Task<(RuntimeMutationSpecification? Specification, GameDefinition? Definition)> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(gameServerId.ToString(), cancellationToken);
        if (server is null || server.InstallationType != ManagedInstallationTypes.Managed
            || !_definitions.TryGet(new GameId(server.GameId), out var definition)) return (null, null);
        var operation = _operations is null ? null : await _operations.GetAsync(operationId, cancellationToken);
        VerifiedRuntimeImage? verifiedImage = null;
        var image = definition!.LegacyRuntimeImages.SingleOrDefault(item => item.RuntimeType == server.RuntimeType);
        if (operation?.PipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion)
        {
            if (_images is null) return (null, definition);
            try
            {
                verifiedImage = await _images.LoadVerifiedAsync(new(operationId, server.Id, server.GameId, server.RuntimeType), cancellationToken);
                image = verifiedImage.ApprovedImage;
            }
            catch (InvalidOperationException) { return (null, definition); }
        }
        if (image is null) return (null, definition);
        var ports = server.PortReservations.Where(item => item.ProvisioningOperationId == operationId
                && item.GameServerId == server.Id && item.Status == ReservationStatuses.Reserved)
            .Select(item => new RuntimePortBinding(item.Id, item.PortDefinitionId, item.Protocol,
                item.ContainerPort, item.HostPort, item.Published, item.Exposure)).ToArray();
        var mounts = server.StorageReservations.Where(item => item.ProvisioningOperationId == operationId
                && item.GameServerId == server.Id && item.Status == ReservationStatuses.Reserved
                && item.Ownership == StorageOwnerships.Managed)
            .Select(item =>
            {
                var storage = definition.Storages.SingleOrDefault(candidate => candidate.Id == item.StorageDefinitionId);
                if (storage?.RuntimeTarget is null) return null;
                var historical = _paths.CreateLayout(gameServerId);
                var apiPath = ManagedStorageTargetBuilder.ResolvePersistedPath(item.ApiPath, item.RelativePath, historical.DataRoot);
                var hostPath = ManagedStorageTargetBuilder.ResolvePersistedPath(item.HostPath, item.RelativePath, historical.HostRoot);
                return new RuntimeStorageMount(item.Id, item.StorageDefinitionId, hostPath,
                    storage.RuntimeTarget, false, apiPath,
                    ManagedStorageTargetBuilder.GetPersistedRoot(hostPath, item.RelativePath));
            }).Where(item => item is not null).Cast<RuntimeStorageMount>().ToArray();
        if (ports.Length == 0 || mounts.Length == 0) return (null, definition);
        var requirements = definition.Requirements;
        var environment = definition.RuntimeEnvironment.Where(item => item.RuntimeType == server.RuntimeType).ToArray();
        return (new RuntimeMutationSpecification(gameServerId, definition.GameId, operationId, server.RuntimeType, image,
            ports, mounts, [], environment, new(requirements?.MinimumLogicalProcessors ?? 1, requirements?.Memory?.MinimumBytes ?? 1),
            RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged, verifiedImage), definition);
    }
}
