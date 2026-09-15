using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IProvisioningReconciliationService
{
    Task<ProvisioningOperationSnapshot> ApplyAsync(string operationId, string stepId, int expectedVersion,
        CancellationToken cancellationToken = default);
}

// Internal application service: callers select an operation, never supply evidence or arbitrary outcomes.
public sealed class ProvisioningReconciliationService(IProvisioningOperationStore operations,
    IPersistenceTransactionBoundary transactions, IEnumerable<IProvisioningStepReconciler> reconcilers)
    : IProvisioningReconciliationService
{
    public async Task<ProvisioningOperationSnapshot> ApplyAsync(string operationId, string stepId, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await operations.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("Provisioning operation is missing.");
        if (snapshot.Version != expectedVersion) throw new ProvisioningConcurrencyException("Reconciliation version conflict.");
        var definition = ProvisioningPipelines.Find(snapshot.PipelineVersion)
            ?? throw new ProvisioningTransitionException("Unsupported provisioning pipeline.");
        if (!definition.Matches(snapshot)) throw new ProvisioningTransitionException("Persisted pipeline metadata is invalid.");
        var step = snapshot.Steps.Single(item => item.StepId == stepId);
        if (!snapshot.IsActive
            || snapshot.Status is not ProvisioningOperationStatuses.Running and not ProvisioningOperationStatuses.Failed
                and not ProvisioningOperationStatuses.Cancelled
            || step.RetryClassification != ProvisioningRetryClassifications.RequiresInspection
            || step.SideEffectClassification == ProvisioningSideEffectClassifications.ReadOnly
            || step.Status is not ProvisioningStepStatuses.Running and not ProvisioningStepStatuses.Failed
            || step.ReconciledRetryAttempt is not null
            || snapshot.Steps.Any(item => item.Sequence < step.Sequence
                && item.Status is not ProvisioningStepStatuses.Succeeded and not ProvisioningStepStatuses.Skipped))
            throw new ProvisioningTransitionException("Step is not eligible for reconciliation application.");
        var reconciler = reconcilers.SingleOrDefault(item => item.StepId == stepId)
            ?? throw new ProvisioningTransitionException("No trusted reconciler is registered.");
        ProvisioningReconciliationResult evidence;
        try { evidence = await reconciler.InspectAsync(snapshot, step, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            evidence = new(ProvisioningReconciliationOutcomes.Ambiguous, "Inspection failed.");
        }
        var outcome = evidence.Outcome is ProvisioningReconciliationOutcomes.EffectExists or ProvisioningReconciliationOutcomes.EffectAbsent
            ? evidence.Outcome : ProvisioningReconciliationOutcomes.Ambiguous;
        var observedAt = DateTimeOffset.UtcNow;
        try
        {
            return await transactions.ExecuteAsync(async (db, token) =>
            {
                var operation = await db.ProvisioningOperations.Include(item => item.Steps)
                    .SingleAsync(item => item.Id == operationId, token);
                if (operation.Version != expectedVersion) throw new ProvisioningConcurrencyException("Reconciliation evidence is stale.");
                var persisted = operation.Steps.Single(item => item.StepId == stepId);
                var terminalCancellation = operation.Status == ProvisioningOperationStatuses.Cancelled;
                if (outcome == ProvisioningReconciliationOutcomes.EffectExists && stepId == ProvisioningStepIds.AcquireImage)
                {
                    var image = await db.RuntimeImageIntents.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == operationId, token);
                    if (image is null || RuntimeImageIntentStore.Map(image).VerifiedLocalImageId is null)
                        throw new ProvisioningTransitionException("Acquisition cannot complete without durable verified identity.");
                }
                var code = outcome switch
                {
                    ProvisioningReconciliationOutcomes.EffectExists => "reconciled_effect_exists",
                    ProvisioningReconciliationOutcomes.EffectAbsent when terminalCancellation => "reconciled_cancelled_effect_absent",
                    ProvisioningReconciliationOutcomes.EffectAbsent when step.Attempt < step.MaxAttempts => "reconciled_retry_authorized",
                    ProvisioningReconciliationOutcomes.EffectAbsent => "reconciled_attempts_exhausted",
                    _ => "reconciled_effect_ambiguous"
                };
                db.ProvisioningReconciliations.Add(new()
                {
                    Id = Guid.NewGuid().ToString("N"), OperationId = operationId, StepId = stepId,
                    OperationVersion = expectedVersion, Attempt = step.Attempt, Outcome = outcome,
                    PriorStatus = persisted.Status, PriorFailureType = persisted.FailureType,
                    PriorErrorCode = persisted.ErrorCode, AppliedCode = code, ObservedAtUtc = observedAt
                });
                persisted.ErrorCode = code;
                persisted.SafeErrorMessage = "Trusted inspection was applied to the provisioning checkpoint.";
                persisted.CompletedAtUtc = observedAt;
                persisted.ReconciledRetryAttempt = null;
                if (outcome == ProvisioningReconciliationOutcomes.EffectExists)
                {
                    persisted.Status = ProvisioningStepStatuses.Succeeded;
                    persisted.FailureType = null;
                    if (!terminalCancellation) operation.Status = ProvisioningOperationStatuses.Running;
                    // Applied effects stay protected, including explicitly cancelled operations.
                    operation.ActiveSlot = ProvisioningOperationActiveSlots.Active;
                }
                else if (outcome == ProvisioningReconciliationOutcomes.EffectAbsent)
                {
                    persisted.Status = ProvisioningStepStatuses.Failed;
                    persisted.FailureType = ProvisioningFailureTypes.Transient;
                    if (!terminalCancellation && step.Attempt < step.MaxAttempts)
                    {
                        persisted.ReconciledRetryAttempt = step.Attempt + 1;
                        operation.Status = ProvisioningOperationStatuses.Running;
                        operation.ActiveSlot = ProvisioningOperationActiveSlots.Active;
                    }
                    else
                    {
                        if (!terminalCancellation) operation.Status = ProvisioningOperationStatuses.Failed;
                        operation.ActiveSlot = null;
                    }
                }
                else
                {
                    persisted.Status = ProvisioningStepStatuses.Failed;
                    persisted.FailureType = ProvisioningFailureTypes.Unknown;
                    if (!terminalCancellation) operation.Status = ProvisioningOperationStatuses.Failed;
                    operation.ActiveSlot = ProvisioningOperationActiveSlots.Active;
                }
                operation.Version++;
                operation.CurrentStep = stepId;
                operation.ErrorCode = code;
                operation.ErrorMessageSafe = persisted.SafeErrorMessage;
                operation.CompletedAtUtc = operation.Status == ProvisioningOperationStatuses.Running ? null : observedAt;
                return ProvisioningOperationStore.Map(operation);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ProvisioningConcurrencyException("Reconciliation changed concurrently.", exception);
        }
    }
}
