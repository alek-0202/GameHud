using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Provisioning;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IProvisioningContextLoader
{
    Task<ProvisioningContext> LoadAsync(string operationId, CancellationToken cancellationToken);
}

public sealed class ProvisioningContextLoader(
    IProvisioningOperationStore operations,
    IManagedServerStore servers,
    IGameDefinitionRegistry definitions) : IProvisioningContextLoader
{
    public async Task<ProvisioningContext> LoadAsync(string operationId, CancellationToken cancellationToken)
    {
        var operation = await operations.GetAsync(operationId, cancellationToken)
            ?? throw new ProvisioningTransitionException("Provisioning operation is missing.");
        var pipeline = ProvisioningPipelines.Find(operation.PipelineVersion)
            ?? throw new ProvisioningTransitionException("Persisted provisioning pipeline is unsupported.");
        if (!pipeline.Matches(operation))
            throw new ProvisioningTransitionException("Persisted provisioning pipeline metadata is invalid.");

        var server = await servers.GetManagedServerAsync(operation.GameServerId, cancellationToken)
            ?? throw new ProvisioningTransitionException("Managed server is missing.");
        if (!definitions.TryGet(new GameId(server.GameId), out var definition))
            throw new ProvisioningTransitionException("Persisted game definition is unavailable.");

        var portReservations = server.PortReservations
            .Where(item => item.ProvisioningOperationId == operationId)
            .OrderBy(item => item.PortDefinitionId, StringComparer.Ordinal)
            .ToArray();
        var storageReservations = server.StorageReservations
            .Where(item => item.ProvisioningOperationId == operationId)
            .OrderBy(item => item.StorageDefinitionId, StringComparer.Ordinal)
            .ToArray();
        if (portReservations.Length == 0 || storageReservations.Length == 0)
            throw new ProvisioningTransitionException("Durable provisioning reservations are incomplete.");

        var plan = new ValidatedProvisioningPlan(
            new GameServerId(server.Id),
            new GameId(server.GameId),
            server.DisplayName,
            server.RuntimeType,
            "durable_recovery",
            [],
            portReservations.Select(item => new ValidatedProvisioningPort(
                item.PortDefinitionId, item.Protocol, item.ContainerPort, item.HostPort, item.Published, item.Exposure)).ToArray(),
            storageReservations.Select(item => new ValidatedProvisioningStorage(
                item.StorageDefinitionId, item.RelativePath, item.ApiPath, item.HostPath)).ToArray(),
            [],
            pipeline.Steps.Select(item => item.Id).ToArray());
        var reservation = new ManagedServerReservationResult(
            server.Id,
            operationId,
            portReservations.Select(item => item.Id).ToArray(),
            storageReservations.Select(item => item.Id).ToArray());

        return new ProvisioningContext(operationId, definition!, plan, reservation, userRequestedCancellation: false);
    }
}
