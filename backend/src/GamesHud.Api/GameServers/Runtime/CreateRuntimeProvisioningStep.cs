using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.GameServers.Runtime;

public sealed class CreateRuntimeProvisioningStep : IProvisioningStep
{
    private readonly IRuntimeSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IRuntimeMutationExecutor _executor;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly ILogger<CreateRuntimeProvisioningStep> _logger;

    public CreateRuntimeProvisioningStep(IRuntimeSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IRuntimeMutationExecutor executor, IManagedStoragePathBuilder paths, ILogger<CreateRuntimeProvisioningStep> logger)
    {
        _builder = builder;
        _policy = policy;
        _executor = executor;
        _paths = paths;
        _logger = logger;
    }

    public string Id => ProvisioningStepIds.CreateRuntime;

    public async Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var specification = await _builder.BuildAsync(context, cancellationToken);
        if (specification is null)
            return ProvisioningStepResult.Failure(RuntimePolicyErrorCodes.RuntimePolicyDenied, "Runtime policy denied the mutation.");

        var root = _paths.CreateLayout(context.GameServerId).DataRoot;
        var result = _policy.Validate(specification, context.GameDefinition, root);
        _logger.LogInformation("Runtime policy for operation {OperationId}, server {GameServerId}, runtime {RuntimeType}: {PolicyResult} {ViolationCodes}",
            context.OperationId, context.GameServerId, specification.RuntimeType, result.Allowed ? "allowed" : "denied",
            result.Violations.Select(item => item.Code).ToArray());

        if (!result.Allowed)
            return ProvisioningStepResult.Failure(RuntimePolicyErrorCodes.RuntimePolicyDenied, "Runtime policy denied the mutation.");

        var executionContext = new RuntimeMutationExecutionContext(
            result.Specification!, RuntimeMutationKind.CreateRuntime, ProvisioningStepIds.CreateRuntime, attempt: 1);
        var execution = await _executor.ExecuteCreateAsync(executionContext, cancellationToken);
        return execution.Status switch
        {
            RuntimeMutationOutcomeStatuses.Success => ProvisioningStepResult.Skipped(execution.SafeMessage!),
            RuntimeMutationOutcomeStatuses.CancelledBeforeInvocation => throw new OperationCanceledException(cancellationToken),
            RuntimeMutationOutcomeStatuses.KnownFailure => ProvisioningStepResult.Failure(
                execution.SafeCode!, execution.SafeMessage!, ProvisioningFailureTypes.Transient),
            _ => ProvisioningStepResult.Failure(
                execution.SafeCode ?? RuntimeMutationExecutionErrorCodes.ProviderOutcomeUnknown,
                execution.SafeMessage ?? "Runtime provider outcome is unknown.", ProvisioningFailureTypes.Unknown)
        };
    }
}
