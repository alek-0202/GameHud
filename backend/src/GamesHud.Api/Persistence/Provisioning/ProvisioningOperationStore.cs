using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence.Provisioning;

public sealed class ProvisioningOperationStore : IProvisioningOperationStore
{
    private readonly GamesHudDbContext _dbContext;
    private readonly IPersistenceTransactionBoundary _transactions;
    private readonly IProvisioningStateMachine _stateMachine;

    public ProvisioningOperationStore(
        GamesHudDbContext dbContext,
        IPersistenceTransactionBoundary transactions,
        IProvisioningStateMachine stateMachine)
    {
        _dbContext = dbContext;
        _transactions = transactions;
        _stateMachine = stateMachine;
    }

    public async Task<ProvisioningOperationSnapshot?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operation = await _dbContext.ProvisioningOperations
            .AsNoTracking()
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(item => item.Id == operationId.Trim(), cancellationToken);
        return operation is null ? null : Map(operation);
    }

    public async Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        var operations = await _dbContext.ProvisioningOperations
            .AsNoTracking()
            .Include(item => item.Steps)
            .Where(item => item.ActiveSlot == ProvisioningOperationActiveSlots.Active)
            .ToArrayAsync(cancellationToken);
        return operations.OrderBy(item => item.StartedAtUtc).Select(Map).ToArray();
    }

    public async Task<ProvisioningOperationSnapshot> ApplyCheckpointAsync(
        ProvisioningCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        try
        {
            return await _transactions.ExecuteAsync(async (database, token) =>
            {
                var operation = await database.ProvisioningOperations
                    .Include(item => item.Steps)
                    .SingleAsync(item => item.Id == checkpoint.OperationId, token);

                if (operation.Version != checkpoint.ExpectedVersion)
                {
                    throw new ProvisioningConcurrencyException(
                        $"Provisioning operation version conflict. Expected {checkpoint.ExpectedVersion}, found {operation.Version}.");
                }

                _stateMachine.EnsureOperationTransition(
                    operation.Status,
                    checkpoint.OperationStatus,
                    checkpoint.ExplicitRetry);

                var now = DateTimeOffset.UtcNow;
                ProvisioningStepRecord? step = null;
                if (checkpoint.StepId is not null && checkpoint.StepStatus is not null)
                {
                    step = operation.Steps.Single(item => item.StepId == checkpoint.StepId);
                    var snapshot = Map(step);
                    if (checkpoint.StepStatus == ProvisioningStepStatuses.Running
                        && step.Status == ProvisioningStepStatuses.Pending
                        && operation.Steps.Any(item =>
                            item.Sequence < step.Sequence
                            && item.Status is not ProvisioningStepStatuses.Succeeded
                                and not ProvisioningStepStatuses.Skipped
                                and not ProvisioningStepStatuses.Compensated))
                    {
                        throw new ProvisioningTransitionException(
                            $"Provisioning step '{step.StepId}' cannot start before earlier pipeline steps complete.");
                    }
                    _stateMachine.EnsureStepTransition(snapshot, checkpoint.StepStatus, checkpoint.ExplicitRetry);
                    ApplyStepCheckpoint(step, checkpoint, now);
                }

                operation.Status = checkpoint.OperationStatus;
                operation.CurrentStep = checkpoint.CurrentStep;
                operation.Version++;
                operation.ErrorCode = NormalizeOptional(checkpoint.ErrorCode, 120);
                operation.ErrorMessageSafe = NormalizeOptional(checkpoint.SafeErrorMessage, 500);

                if (checkpoint.OperationStatus is ProvisioningOperationStatuses.Succeeded
                    or ProvisioningOperationStatuses.Failed
                    or ProvisioningOperationStatuses.Cancelled
                    or ProvisioningOperationStatuses.CompensationFailed)
                {
                    operation.CompletedAtUtc = now;
                    operation.ActiveSlot = checkpoint.KeepActiveSlot
                        || checkpoint.OperationStatus == ProvisioningOperationStatuses.CompensationFailed
                        ? ProvisioningOperationActiveSlots.Active
                        : null;
                }
                else
                {
                    operation.CompletedAtUtc = null;
                    operation.ActiveSlot = ProvisioningOperationActiveSlots.Active;
                }

                await Task.CompletedTask;
                return Map(operation);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ProvisioningConcurrencyException(
                "Provisioning operation was advanced by another worker.",
                exception);
        }
    }

    private static void ApplyStepCheckpoint(
        ProvisioningStepRecord step,
        ProvisioningCheckpoint checkpoint,
        DateTimeOffset now)
    {
        step.Status = checkpoint.StepStatus!;
        step.FailureType = NormalizeOptional(checkpoint.FailureType, 40);
        step.ErrorCode = NormalizeOptional(checkpoint.ErrorCode, 120);
        step.SafeErrorMessage = NormalizeOptional(checkpoint.SafeErrorMessage, 500);

        if (checkpoint.StepStatus == ProvisioningStepStatuses.Running)
        {
            step.Attempt++;
            step.StartedAtUtc = now;
            step.CompletedAtUtc = null;
        }
        else if (checkpoint.StepStatus is ProvisioningStepStatuses.Succeeded
            or ProvisioningStepStatuses.Failed
            or ProvisioningStepStatuses.Skipped)
        {
            step.CompletedAtUtc = now;
        }
        else if (checkpoint.StepStatus == ProvisioningStepStatuses.Compensating)
        {
            step.CompensationStartedAtUtc = now;
            step.CompensationCompletedAtUtc = null;
        }
        else if (checkpoint.StepStatus is ProvisioningStepStatuses.Compensated
            or ProvisioningStepStatuses.CompensationFailed)
        {
            step.CompensationCompletedAtUtc = now;
        }
    }

    internal static ProvisioningOperationSnapshot Map(ProvisioningOperationRecord operation) =>
        new(
            operation.Id,
            operation.GameServerId,
            operation.Status,
            operation.CurrentStep,
            operation.ErrorCode,
            operation.ErrorMessageSafe,
            operation.StartedAtUtc,
            operation.CompletedAtUtc,
            operation.PipelineVersion,
            operation.Version,
            operation.ActiveSlot == ProvisioningOperationActiveSlots.Active,
            operation.Steps.OrderBy(step => step.Sequence).Select(Map).ToArray());

    internal static ProvisioningStepSnapshot Map(ProvisioningStepRecord step) =>
        new(
            step.StepId,
            step.Sequence,
            step.Status,
            step.Attempt,
            step.RetryClassification,
            step.SideEffectClassification,
            step.MaxAttempts,
            step.StartedAtUtc,
            step.CompletedAtUtc,
            step.FailureType,
            step.ErrorCode,
            step.SafeErrorMessage,
            step.CompensationStartedAtUtc,
            step.CompensationCompletedAtUtc);

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
