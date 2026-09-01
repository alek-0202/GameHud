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
            RuntimeMutationOutcomeStatuses.Success => ProvisioningStepResult.Success(),
            RuntimeMutationOutcomeStatuses.CancelledBeforeInvocation => throw new OperationCanceledException(cancellationToken),
            RuntimeMutationOutcomeStatuses.KnownFailure => ProvisioningStepResult.Failure(
                execution.SafeCode!, execution.SafeMessage!, ProvisioningFailureTypes.Transient),
            _ => ProvisioningStepResult.Failure(
                execution.SafeCode ?? RuntimeMutationExecutionErrorCodes.ProviderOutcomeUnknown,
                execution.SafeMessage ?? "Runtime provider outcome is unknown.", ProvisioningFailureTypes.Unknown)
        };
    }
}

internal sealed class CreateRuntimeReconciler : IProvisioningStepReconciler
{
    private readonly IRuntimeReconciliationSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly DockerGameRuntimeAdapter _adapter;

    public CreateRuntimeReconciler(IRuntimeReconciliationSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IManagedStoragePathBuilder paths, DockerGameRuntimeAdapter adapter)
    {
        _builder = builder;
        _policy = policy;
        _paths = paths;
        _adapter = adapter;
    }

    public string StepId => ProvisioningStepIds.CreateRuntime;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation, ProvisioningStepSnapshot step, CancellationToken cancellationToken)
    {
        var gameServerId = new GamesHud.Api.GameServers.Domain.GameServerId(operation.GameServerId);
        var built = await _builder.BuildForReconciliationAsync(operation.OperationId, gameServerId, cancellationToken);
        if (built.Specification is null || built.Definition is null)
            return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed runtime identity could not be reconstructed safely.");
        var root = _paths.CreateLayout(gameServerId).DataRoot;
        var validated = _policy.Validate(built.Specification, built.Definition, root);
        if (!validated.Allowed)
            return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed runtime identity could not be reconstructed safely.");
        var context = new RuntimeMutationExecutionContext(validated.Specification!, RuntimeMutationKind.CreateRuntime,
            ProvisioningStepIds.CreateRuntime, Math.Max(step.Attempt, 1));
        return await _adapter.ReconcileAsync(context, cancellationToken);
    }
}
