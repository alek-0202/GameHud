using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IProvisioningStep
{
    string Id { get; }
    Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken);
}

public interface ICompensatingProvisioningStep
{
    Task CompensateAsync(ProvisioningContext context, CancellationToken cancellationToken);
}

public interface IProvisioningEngine
{
    Task<ProvisioningExecutionResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken);
}

public sealed class ProvisioningEngine : IProvisioningEngine
{
    private readonly IProvisioningOperationStore _operations;
    private readonly IReadOnlyDictionary<string, IProvisioningStep> _steps;
    private readonly ILogger<ProvisioningEngine> _logger;

    public ProvisioningEngine(
        IProvisioningOperationStore operations,
        IEnumerable<IProvisioningStep> steps,
        ILogger<ProvisioningEngine> logger)
    {
        _operations = operations;
        _steps = steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        _logger = logger;
    }

    public async Task<ProvisioningExecutionResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var state = await _operations.GetAsync(context.OperationId, CancellationToken.None)
            ?? throw new InvalidOperationException("The persisted provisioning operation was not found.");
        if (state.PipelineVersion != ProvisioningPipeline.Version)
        {
            throw new InvalidOperationException("The persisted provisioning pipeline version is not supported.");
        }

        foreach (var persistedStep in state.Steps
            .Where(step => ProvisioningStepIds.ExecutableFoundation.Contains(step.StepId, StringComparer.Ordinal))
            .OrderBy(step => step.Sequence))
        {
            var stepId = persistedStep.StepId;
            if (persistedStep.Status is ProvisioningStepStatuses.Succeeded
                or ProvisioningStepStatuses.Skipped
                or ProvisioningStepStatuses.Compensated)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return await CancelAsync(state, context, null);
            }

            try
            {
                state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                    context.OperationId,
                    state.Version,
                    ProvisioningOperationStatuses.Running,
                    stepId,
                    stepId,
                    ProvisioningStepStatuses.Running), cancellationToken);

                var step = _steps[stepId];
                var result = await step.ExecuteAsync(context, cancellationToken);
                context.Record(stepId, result.Status);
                _logger.LogInformation(
                    "Provisioning operation {OperationId} for server {GameServerId} completed step {StepId} with {Result}",
                    context.OperationId, context.GameServerId, stepId, result.Status);

                var persistedStatus = result.Status switch
                {
                    ProvisioningStepResultStatuses.Succeeded => ProvisioningStepStatuses.Succeeded,
                    ProvisioningStepResultStatuses.Skipped => ProvisioningStepStatuses.Skipped,
                    ProvisioningStepResultStatuses.Failed => ProvisioningStepStatuses.Failed,
                    _ => throw new InvalidOperationException("Provisioning step returned an unsupported status.")
                };
                state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                    context.OperationId,
                    state.Version,
                    ProvisioningOperationStatuses.Running,
                    stepId,
                    stepId,
                    persistedStatus,
                    result.FailureType,
                    result.ErrorCode,
                    result.SafeMessage), CancellationToken.None);

                if (persistedStatus == ProvisioningStepStatuses.Failed)
                {
                    return await FailAndCompensateAsync(
                        state,
                        context,
                        stepId,
                        result.ErrorCode ?? ProvisioningErrorCodes.StepFailed,
                        result.SafeMessage ?? "A provisioning step failed.");
                }
            }
            catch (OperationCanceledException)
            {
                return await CancelAsync(state, context, stepId);
            }
            catch (ProvisioningConcurrencyException)
            {
                throw;
            }
            catch (ProvisioningTransitionException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Provisioning operation {OperationId} for server {GameServerId} failed at step {StepId} with exception type {ExceptionType}",
                    context.OperationId, context.GameServerId, stepId, exception.GetType().Name);
                return await InterruptAsync(state, context, stepId);
            }
        }

        state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            context.OperationId,
            state.Version,
            ProvisioningOperationStatuses.Succeeded,
            ProvisioningStepIds.Complete), CancellationToken.None);

        return new ProvisioningExecutionResult(
            true,
            context.OperationId,
            ProvisioningOperationStatuses.Succeeded,
            null);
    }

    private async Task<ProvisioningExecutionResult> FailAndCompensateAsync(
        ProvisioningOperationSnapshot state,
        ProvisioningContext context,
        string failedStepId,
        string code,
        string message)
    {
        var compensations = state.Steps
            .Where(step => step.Status == ProvisioningStepStatuses.Succeeded
                && _steps.TryGetValue(step.StepId, out var runtimeStep)
                && runtimeStep is ICompensatingProvisioningStep)
            .OrderByDescending(step => step.Sequence)
            .ToArray();

        if (compensations.Length > 0)
        {
            state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                context.OperationId,
                state.Version,
                ProvisioningOperationStatuses.Compensating,
                failedStepId,
                ErrorCode: code,
                SafeErrorMessage: message), CancellationToken.None);

            foreach (var persistedStep in compensations)
            {
                state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                    context.OperationId,
                    state.Version,
                    ProvisioningOperationStatuses.Compensating,
                    persistedStep.StepId,
                    persistedStep.StepId,
                    ProvisioningStepStatuses.Compensating), CancellationToken.None);
                try
                {
                    await ((ICompensatingProvisioningStep)_steps[persistedStep.StepId])
                        .CompensateAsync(context, CancellationToken.None);
                    state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                        context.OperationId,
                        state.Version,
                        ProvisioningOperationStatuses.Compensating,
                        persistedStep.StepId,
                        persistedStep.StepId,
                        ProvisioningStepStatuses.Compensated), CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        "Provisioning compensation for operation {OperationId} failed at step {StepId} with exception type {ExceptionType}",
                        context.OperationId,
                        persistedStep.StepId,
                        exception.GetType().Name);
                    state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                        context.OperationId,
                        state.Version,
                        ProvisioningOperationStatuses.CompensationFailed,
                        persistedStep.StepId,
                        persistedStep.StepId,
                        ProvisioningStepStatuses.CompensationFailed,
                        ProvisioningFailureTypes.Unknown,
                        "compensation_failed",
                        "Provisioning compensation did not complete.",
                        KeepActiveSlot: true), CancellationToken.None);
                    return FailureResult(context, ProvisioningOperationStatuses.CompensationFailed,
                        "compensation_failed", "Provisioning compensation did not complete.");
                }
            }
        }

        state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            context.OperationId,
            state.Version,
            ProvisioningOperationStatuses.Failed,
            failedStepId,
            ErrorCode: code,
            SafeErrorMessage: message), CancellationToken.None);

        return FailureResult(context, ProvisioningOperationStatuses.Failed, code, message);
    }

    private async Task<ProvisioningExecutionResult> InterruptAsync(
        ProvisioningOperationSnapshot state,
        ProvisioningContext context,
        string stepId)
    {
        var step = state.Steps.Single(item => item.StepId == stepId);
        state = await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            context.OperationId,
            state.Version,
            ProvisioningOperationStatuses.Failed,
            stepId,
            stepId,
            ProvisioningStepStatuses.Failed,
            ProvisioningFailureTypes.Unknown,
            ProvisioningErrorCodes.StepFailed,
            "An unexpected provisioning error occurred.",
            KeepActiveSlot: step.SideEffectClassification != ProvisioningSideEffectClassifications.ReadOnly), CancellationToken.None);

        return FailureResult(context, ProvisioningOperationStatuses.Failed,
            ProvisioningErrorCodes.StepFailed, "An unexpected provisioning error occurred.");
    }

    private async Task<ProvisioningExecutionResult> CancelAsync(
        ProvisioningOperationSnapshot state,
        ProvisioningContext context,
        string? runningStepId)
    {
        ProvisioningStepSnapshot? step = runningStepId is null
            ? null
            : state.Steps.Single(item => item.StepId == runningStepId);
        var failureType = step is null
            ? null
            : step.SideEffectClassification == ProvisioningSideEffectClassifications.ReadOnly
                ? ProvisioningFailureTypes.Permanent
                : ProvisioningFailureTypes.Unknown;
        await _operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            context.OperationId,
            state.Version,
            ProvisioningOperationStatuses.Cancelled,
            runningStepId ?? state.CurrentStep,
            runningStepId,
            runningStepId is null ? null : ProvisioningStepStatuses.Failed,
            failureType,
            ProvisioningErrorCodes.ProvisioningCancelled,
            "Provisioning was cancelled.",
            KeepActiveSlot: step is not null
                && step.SideEffectClassification != ProvisioningSideEffectClassifications.ReadOnly), CancellationToken.None);

        return FailureResult(context, ProvisioningOperationStatuses.Cancelled,
            ProvisioningErrorCodes.ProvisioningCancelled, "Provisioning was cancelled.");
    }

    private static ProvisioningExecutionResult FailureResult(
        ProvisioningContext context,
        string status,
        string code,
        string message)
    {

        return new ProvisioningExecutionResult(
            false,
            context.OperationId,
            status,
            new ProvisioningFailure(code, message));
    }
}

public sealed class NoHostMutationProvisioningStep : IProvisioningStep
{
    public NoHostMutationProvisioningStep(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public Task<ProvisioningStepResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Id == ProvisioningStepIds.Complete
            ? ProvisioningStepResult.Success()
            : ProvisioningStepResult.Skipped("Host mutation is not implemented in GH-09."));
    }
}
