using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IGameServerProvisioningService
{
    Task<ProvisioningPreviewResult> PreviewAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken);
    Task<ProvisioningExecutionResult> ScheduleProvisioningAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken);
    Task<ProvisioningExecutionResult> StartProvisioningAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken);
    Task<IdempotentProvisioningExecutionResult> ScheduleIdempotentProvisioningAsync(
        CreateGameServerProvisioningRequest request, string idempotencyKeyHash,
        string requestFingerprint, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(CancellationToken cancellationToken);
}

public sealed class GameServerProvisioningService : IGameServerProvisioningService
{
    private readonly IProvisioningPlanBuilder _planBuilder;
    private readonly IManagedServerStore _store;
    private readonly IProvisioningOperationStore _operations;
    private readonly IProvisioningExecutionSignal? _executionSignal;

    public GameServerProvisioningService(
        IProvisioningPlanBuilder planBuilder,
        IManagedServerStore store,
        IProvisioningOperationStore operations,
        IProvisioningEngine engine,
        IProvisioningExecutionSignal? executionSignal = null)
    {
        _planBuilder = planBuilder;
        _store = store;
        _operations = operations;
        _ = engine;
        _executionSignal = executionSignal;
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
        CancellationToken cancellationToken) =>
        await ScheduleProvisioningAsync(request, cancellationToken);

    public async Task<ProvisioningExecutionResult> ScheduleProvisioningAsync(
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

        var selection = ProvisioningPipelines.SelectForManaged(planResult.Definition!, plan.RuntimeType);
        var persistencePlan = new ManagedServerProvisioningPlan(
            plan.GameServerId.ToString(),
            plan.GameId.ToString(),
            plan.DisplayName,
            plan.RuntimeType,
            plan.Ports.Select(port => new PortReservationPlan(
                port.DefinitionId, port.Protocol, port.ContainerPort, port.HostPort, port.Published, port.Exposure)).ToArray(),
            plan.Storage.Select(storage => new StorageReservationPlan(
                storage.DefinitionId, storage.RelativePath, storage.ApiPath, storage.HostPath)).ToArray(),
            plan.GameConfiguration,
            selection.Pipeline.Version,
            selection.Pipeline.Steps.Select(step => new ProvisioningStepPlan(
                step.Id,
                step.Sequence,
                step.RetryClassification,
                step.SideEffectClassification,
                step.MaxAttempts,
                step.Sequence <= 3)).ToArray(),
            selection.RuntimeImage);
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

        _executionSignal?.Signal();
        return new ProvisioningExecutionResult(
            true,
            reservation.ProvisioningOperationId,
            ProvisioningOperationStatuses.Pending,
            null);
    }

    public async Task<IdempotentProvisioningExecutionResult> ScheduleIdempotentProvisioningAsync(
        CreateGameServerProvisioningRequest request,
        string idempotencyKeyHash,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetRequestAsync(idempotencyKeyHash, cancellationToken);
        if (existing is not null)
            return Existing(existing, requestFingerprint);

        var planResult = await _planBuilder.BuildAsync(request, cancellationToken);
        if (!planResult.Succeeded)
            return IdempotentFailed(planResult.Failure!);

        var plan = CreatePersistencePlan(planResult);
        var conflict = await _store.FindReservationConflictAsync(plan, cancellationToken);
        if (conflict is not null)
            return IdempotentFailed(new ProvisioningFailure(conflict.Code, conflict.SafeMessage));

        try
        {
            var result = await _store.ReserveIdempotentProvisioningPlanAsync(plan,
                new ManagedServerRequestIdentity(idempotencyKeyHash, requestFingerprint), cancellationToken);
            if (result.Created) _executionSignal?.Signal();
            return Existing(result, requestFingerprint);
        }
        catch (DbUpdateException)
        {
            var winner = await _store.GetRequestAsync(idempotencyKeyHash, cancellationToken);
            if (winner is not null) return Existing(winner, requestFingerprint);
            return IdempotentFailed(new ProvisioningFailure(ProvisioningErrorCodes.ReservationFailed,
                "Resources could not be reserved because persisted state changed."));
        }
    }

    private static IdempotentProvisioningExecutionResult Existing(
        ManagedServerRequestResult result, string fingerprint) =>
        result.RequestFingerprint == fingerprint
            ? new(true, result.Created, result.GameServerId, result.ProvisioningOperationId,
                result.RequestFingerprint, ProvisioningOperationStatuses.Pending, null)
            : new(false, false, result.GameServerId, result.ProvisioningOperationId,
                result.RequestFingerprint, ProvisioningOperationStatuses.Failed,
                new ProvisioningFailure("idempotency_conflict",
                    "The idempotency key was already used for a different request."));

    private static IdempotentProvisioningExecutionResult IdempotentFailed(ProvisioningFailure failure) =>
        new(false, false, null, null, null, ProvisioningOperationStatuses.Failed, failure);

    private static ManagedServerProvisioningPlan CreatePersistencePlan(ProvisioningPlanBuildResult planResult)
    {
        var plan = planResult.Plan!;
        var selection = ProvisioningPipelines.SelectForManaged(planResult.Definition!, plan.RuntimeType);
        return new ManagedServerProvisioningPlan(plan.GameServerId.ToString(), plan.GameId.ToString(), plan.DisplayName,
            plan.RuntimeType,
            plan.Ports.Select(port => new PortReservationPlan(port.DefinitionId, port.Protocol, port.ContainerPort,
                port.HostPort, port.Published, port.Exposure)).ToArray(),
            plan.Storage.Select(storage => new StorageReservationPlan(storage.DefinitionId, storage.RelativePath,
                storage.ApiPath, storage.HostPath)).ToArray(), plan.GameConfiguration, selection.Pipeline.Version,
            selection.Pipeline.Steps.Select(step => new ProvisioningStepPlan(step.Id, step.Sequence,
                step.RetryClassification, step.SideEffectClassification, step.MaxAttempts, step.Sequence <= 3)).ToArray(),
            selection.RuntimeImage);
    }

    public async Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(
        CancellationToken cancellationToken)
    {
        return await _operations.GetIncompleteAsync(cancellationToken);
    }

    private static ProvisioningExecutionResult Failed(ProvisioningFailure failure) =>
        new(false, null, ProvisioningOperationStatuses.Failed, failure);
}
