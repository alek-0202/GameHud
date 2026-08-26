using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;

namespace GamesHud.Api.GameServers.Provisioning;

public sealed record ProvisioningReconciliationResult(string Outcome, string SafeMessage);

public interface IProvisioningStepReconciler
{
    string StepId { get; }
    Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step,
        CancellationToken cancellationToken);
}

public interface IProvisioningRecoveryService
{
    Task<IReadOnlyCollection<ProvisioningRecoveryDecision>> ClassifyIncompleteAsync(CancellationToken cancellationToken);
    Task<ProvisioningRecoveryDecision> ClassifyAsync(string operationId, CancellationToken cancellationToken);
    Task<ProvisioningRecoveryDecision> ReconcileAsync(
        string operationId,
        IProvisioningStepReconciler reconciler,
        CancellationToken cancellationToken);
}

public sealed class ProvisioningRecoveryService : IProvisioningRecoveryService
{
    private readonly IProvisioningOperationStore _store;

    public ProvisioningRecoveryService(IProvisioningOperationStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyCollection<ProvisioningRecoveryDecision>> ClassifyIncompleteAsync(
        CancellationToken cancellationToken)
    {
        var operations = await _store.GetIncompleteAsync(cancellationToken);
        return operations.Select(Classify).ToArray();
    }

    public async Task<ProvisioningRecoveryDecision> ClassifyAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = await _store.GetAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provisioning operation was not found.");
        return Classify(operation);
    }

    public async Task<ProvisioningRecoveryDecision> ReconcileAsync(
        string operationId,
        IProvisioningStepReconciler reconciler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        var operation = await _store.GetAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provisioning operation was not found.");
        var step = operation.Steps.Single(item => item.StepId == reconciler.StepId);
        var result = await reconciler.InspectAsync(operation, step, cancellationToken);

        return result.Outcome switch
        {
            ProvisioningReconciliationOutcomes.EffectAbsent => new ProvisioningRecoveryDecision(
                operation.OperationId,
                ProvisioningRecoveryDecisions.Resume,
                "effect_absent",
                "Reconciliation confirmed that the external effect is absent; an explicit retry may be evaluated.",
                step.StepId),
            ProvisioningReconciliationOutcomes.EffectExists => new ProvisioningRecoveryDecision(
                operation.OperationId,
                ProvisioningRecoveryDecisions.ManualIntervention,
                "effect_exists",
                "Reconciliation confirmed an external effect; a future adapter must checkpoint it before continuing.",
                step.StepId),
            _ => new ProvisioningRecoveryDecision(
                operation.OperationId,
                ProvisioningRecoveryDecisions.ManualIntervention,
                "effect_ambiguous",
                "Reconciliation could not prove the external effect state.",
                step.StepId)
        };
    }

    private static ProvisioningRecoveryDecision Classify(ProvisioningOperationSnapshot operation)
    {
        if (operation.PipelineVersion != ProvisioningPipeline.Version)
        {
            return Decision(operation, ProvisioningRecoveryDecisions.ManualIntervention,
                "pipeline_version_mismatch", "The persisted pipeline version differs from the current pipeline.");
        }

        if (operation.Status is ProvisioningOperationStatuses.Compensating
            or ProvisioningOperationStatuses.CompensationFailed)
        {
            var compensationStep = operation.Steps.LastOrDefault(step =>
                step.Status is ProvisioningStepStatuses.Compensating or ProvisioningStepStatuses.CompensationFailed);
            return Decision(operation, ProvisioningRecoveryDecisions.ManualIntervention,
                "compensation_incomplete", "Compensation is incomplete and must not be resumed automatically.", compensationStep?.StepId);
        }

        var uncertain = operation.Steps.FirstOrDefault(step =>
            step.Status == ProvisioningStepStatuses.Running
            || step.Status == ProvisioningStepStatuses.Failed && step.FailureType == ProvisioningFailureTypes.Unknown);
        if (uncertain is not null)
        {
            if (uncertain.SideEffectClassification == ProvisioningSideEffectClassifications.ReadOnly
                && uncertain.RetryClassification == ProvisioningRetryClassifications.SafeToRetry
                && uncertain.Attempt < uncertain.MaxAttempts)
            {
                return Decision(operation, ProvisioningRecoveryDecisions.Resume,
                    "safe_step_retryable", "The interrupted read-only step may be retried explicitly.", uncertain.StepId);
            }

            return Decision(operation, ProvisioningRecoveryDecisions.Reconcile,
                "external_effect_unknown", "The step may have produced an external effect and requires reconciliation.", uncertain.StepId);
        }

        if (operation.Status is ProvisioningOperationStatuses.Succeeded
            or ProvisioningOperationStatuses.Failed
            or ProvisioningOperationStatuses.Cancelled)
        {
            return Decision(operation, ProvisioningRecoveryDecisions.Terminal,
                "operation_terminal", "The provisioning operation is terminal.");
        }

        var next = operation.Steps.FirstOrDefault(step => step.Status == ProvisioningStepStatuses.Pending);
        if (next is not null)
        {
            return Decision(operation, ProvisioningRecoveryDecisions.Resume,
                "next_step_pending", "The next persisted step has not started and may be resumed explicitly.", next.StepId);
        }

        return Decision(operation, ProvisioningRecoveryDecisions.ManualIntervention,
            "state_inconsistent", "The persisted operation state does not identify a safe next action.");
    }

    private static ProvisioningRecoveryDecision Decision(
        ProvisioningOperationSnapshot operation,
        string decision,
        string reason,
        string message,
        string? stepId = null) =>
        new(operation.OperationId, decision, reason, message, stepId);
}

public sealed class ProvisioningRecoveryStartupObserver : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProvisioningRecoveryStartupObserver> _logger;

    public ProvisioningRecoveryStartupObserver(
        IServiceScopeFactory scopeFactory,
        ILogger<ProvisioningRecoveryStartupObserver> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<IProvisioningRecoveryService>();
        var decisions = await recovery.ClassifyIncompleteAsync(cancellationToken);

        foreach (var decision in decisions)
        {
            _logger.LogWarning(
                "Incomplete provisioning operation {OperationId} classified as {Decision} for step {StepId} with reason {ReasonCode}",
                decision.OperationId,
                decision.Decision,
                decision.StepId,
                decision.ReasonCode);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
