using GamesHud.Api.GameServers.Provisioning;

namespace GamesHud.Api.GameServers.Runtime;

public interface IRuntimeMutationExecutor
{
    Task<RuntimeMutationExecutionResult> ExecuteCreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken);
}

public sealed class RuntimeMutationExecutor : IRuntimeMutationExecutor
{
    private readonly IGameRuntimeAdapter _adapter;
    private readonly ILogger<RuntimeMutationExecutor> _logger;

    public RuntimeMutationExecutor(IGameRuntimeAdapter adapter, ILogger<RuntimeMutationExecutor> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<RuntimeMutationExecutionResult> ExecuteCreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
            return Result(context, RuntimeMutationOutcomeStatuses.CancelledBeforeInvocation,
                RuntimeMutationExecutionErrorCodes.CancelledBeforeInvocation, "Runtime mutation was cancelled before provider invocation.",
                ProvisioningRetryClassifications.SafeToRetry, false, false);

        RuntimeProviderOutcome outcome;
        try
        {
            outcome = await _adapter.CreateAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Runtime provider invocation for operation {OperationId}, server {GameServerId}, step {StepId}, mutation {MutationKind}, attempt {Attempt} ended with {ExceptionType}; reconciliation is required",
                context.Specification.Value.OperationId, context.Specification.Value.GameServerId, context.StepId,
                context.MutationKind, context.Attempt, exception.GetType().Name);
            return Result(context, RuntimeMutationOutcomeStatuses.Unknown,
                RuntimeMutationExecutionErrorCodes.ProviderOutcomeUnknown, "Runtime provider outcome is unknown.",
                ProvisioningRetryClassifications.RequiresInspection, true, true);
        }

        var result = outcome.Status switch
        {
            RuntimeMutationOutcomeStatuses.Success => Result(context, outcome.Status, null,
                outcome.SafeMessage ?? "Runtime mutation completed.", ProvisioningRetryClassifications.NonRetryable, false, true),
            RuntimeMutationOutcomeStatuses.KnownFailure => Result(context, outcome.Status,
                outcome.SafeCode ?? RuntimeMutationExecutionErrorCodes.ProviderFailure,
                outcome.SafeMessage ?? "Runtime provider rejected the mutation.", ProvisioningRetryClassifications.SafeToRetry, false, true),
            RuntimeMutationOutcomeStatuses.Unknown => Result(context, outcome.Status,
                outcome.SafeCode ?? RuntimeMutationExecutionErrorCodes.ProviderOutcomeUnknown,
                outcome.SafeMessage ?? "Runtime provider outcome is unknown.", ProvisioningRetryClassifications.RequiresInspection, true, true),
            _ => Result(context, RuntimeMutationOutcomeStatuses.Unknown,
                RuntimeMutationExecutionErrorCodes.ProviderOutcomeUnknown, "Runtime provider outcome is unknown.",
                ProvisioningRetryClassifications.RequiresInspection, true, true)
        };

        _logger.LogInformation(
            "Runtime mutation for operation {OperationId}, server {GameServerId}, step {StepId}, mutation {MutationKind}, attempt {Attempt} completed with {Outcome}; reconciliation required: {ReconciliationRequired}; safe code: {SafeErrorCode}",
            context.Specification.Value.OperationId, context.Specification.Value.GameServerId, context.StepId,
            context.MutationKind, context.Attempt, result.Status, result.ReconciliationRequired, result.SafeCode);
        return result;
    }

    private static RuntimeMutationExecutionResult Result(RuntimeMutationExecutionContext context, string status,
        string? code, string message, string retry, bool reconciliation, bool invoked) =>
        new(status, code, message, retry, reconciliation, invoked, context.MutationExecutionId);
}
