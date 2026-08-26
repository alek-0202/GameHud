using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.Persistence.ManagedServers;

public interface IManagedServerStore
{
    Task<ManagedServerReservationResult> ReserveProvisioningPlanAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default);

    Task<ManagedGameServerRecord?> GetManagedServerAsync(
        string gameServerId,
        CancellationToken cancellationToken = default);

    Task<ProvisioningOperationRecord?> GetActiveOperationAsync(
        string gameServerId,
        CancellationToken cancellationToken = default);

    Task<ManagedServerReservationConflict?> FindReservationConflictAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default);

    Task<ProvisioningOperationRecord?> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProvisioningOperationRecord>> GetIncompleteOperationsAsync(
        CancellationToken cancellationToken = default);

    Task UpdateOperationAsync(
        ProvisioningOperationUpdate update,
        CancellationToken cancellationToken = default);
}
