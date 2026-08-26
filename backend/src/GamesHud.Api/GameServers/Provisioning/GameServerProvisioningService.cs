using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IGameServerProvisioningService
{
    Task<ProvisioningPreviewResult> PreviewAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken);
    Task<ProvisioningExecutionResult> StartProvisioningAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(CancellationToken cancellationToken);
}

public sealed class GameServerProvisioningService : IGameServerProvisioningService
{
    private readonly IProvisioningPlanBuilder _planBuilder;
    private readonly IManagedServerStore _store;
    private readonly IProvisioningOperationStore _operations;
    private readonly IProvisioningEngine _engine;

    public GameServerProvisioningService(
        IProvisioningPlanBuilder planBuilder,
        IManagedServerStore store,
        IProvisioningOperationStore operations,
        IProvisioningEngine engine)
    {
        _planBuilder = planBuilder;
        _store = store;
        _operations = operations;
        _engine = engine;
    }

    public async Task<ProvisioningPreviewResult> PreviewAsync(
        CreateGameServerProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planBuilder.BuildAsync(request, cancellationToken);
        return new ProvisioningPreviewResult(result.Succeeded, result.Plan, result.Failure);
    }

    public async Task<ProvisioningExecutionResult> StartProvisioningAsync(
        CreateGameServerProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        var planResult = await _planBuilder.BuildAsync(request, cancellationToken);
        if (!planResult.Succeeded)
        {
            return Failed(planResult.Failure!);
        }

        var plan = planResult.Plan!;
        if (await _store.GetActiveOperationAsync(plan.GameServerId.ToString(), cancellationToken) is not null)
        {
            return Failed(new ProvisioningFailure(
                ProvisioningErrorCodes.OperationInProgress,
                "A provisioning operation is already active for this server."));
        }

        if (await _store.GetManagedServerAsync(plan.GameServerId.ToString(), cancellationToken) is not null)
        {
            return Failed(new ProvisioningFailure(
                ProvisioningErrorCodes.DuplicateServer,
                "The game server is already managed."));
        }

        var persistencePlan = new ManagedServerProvisioningPlan(
            plan.GameServerId.ToString(),
            plan.GameId.ToString(),
            plan.DisplayName,
            plan.RuntimeType,
            plan.Ports.Select(port => new PortReservationPlan(
                port.DefinitionId, port.Protocol, port.Port, port.Exposure)).ToArray(),
            plan.Storage.Select(storage => new StorageReservationPlan(
                storage.DefinitionId, storage.RelativePath)).ToArray(),
            ProvisioningPipeline.Version,
            ProvisioningPipeline.Steps.Select(step => new ProvisioningStepPlan(
                step.Id,
                step.Sequence,
                step.RetryClassification,
                step.SideEffectClassification,
                step.MaxAttempts,
                step.Sequence <= 3)).ToArray());
        var conflict = await _store.FindReservationConflictAsync(persistencePlan, cancellationToken);
        if (conflict is not null)
        {
            return Failed(new ProvisioningFailure(conflict.Code, conflict.SafeMessage));
        }

        ManagedServerReservationResult reservation;
        try
        {
            reservation = await _store.ReserveProvisioningPlanAsync(persistencePlan, cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failed(new ProvisioningFailure(
                ProvisioningErrorCodes.ReservationFailed,
                "Resources could not be reserved because persisted state changed."));
        }

        var context = new ProvisioningContext(
            reservation.ProvisioningOperationId,
            planResult.Definition!,
            plan,
            reservation);

        return await _engine.ExecuteAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(
        CancellationToken cancellationToken)
    {
        return await _operations.GetIncompleteAsync(cancellationToken);
    }

    private static ProvisioningExecutionResult Failed(ProvisioningFailure failure) =>
        new(false, null, ProvisioningOperationStatuses.Failed, failure);
}
