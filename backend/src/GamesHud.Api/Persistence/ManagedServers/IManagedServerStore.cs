using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.Persistence.ManagedServers;

public interface IManagedServerStore
{
    Task<ManagedServerReservationResult> ReserveProvisioningPlanAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default);

    Task<ManagedServerRequestResult> ReserveIdempotentProvisioningPlanAsync(
        ManagedServerProvisioningPlan plan,
        ManagedServerRequestIdentity requestIdentity,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store does not support HTTP idempotency.");

    Task<ManagedServerRequestResult?> GetRequestAsync(
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default) => Task.FromResult<ManagedServerRequestResult?>(null);

    Task<ManagedGameServerRecord?> GetManagedServerAsync(
        string gameServerId,
        CancellationToken cancellationToken = default);

    Task<ProvisioningOperationRecord?> GetActiveOperationAsync(
        string gameServerId,
        CancellationToken cancellationToken = default);

    Task<ManagedServerReservationConflict?> FindReservationConflictAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default);

}
