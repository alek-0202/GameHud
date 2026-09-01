using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.GameServers.Runtime;

public sealed class CreateRuntimeProvisioningStep : IProvisioningStep
{
    private readonly IRuntimeSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IGameRuntimeAdapter _adapter;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly ILogger<CreateRuntimeProvisioningStep> _logger;

    public CreateRuntimeProvisioningStep(IRuntimeSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IGameRuntimeAdapter adapter, IManagedStoragePathBuilder paths, ILogger<CreateRuntimeProvisioningStep> logger)
    {
        _builder = builder;
        _policy = policy;
        _adapter = adapter;
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

        var adapterResult = await _adapter.CreateAsync(result.Specification!, cancellationToken);
        return adapterResult.Succeeded
            ? ProvisioningStepResult.Skipped(adapterResult.SafeMessage)
            : ProvisioningStepResult.Failure(RuntimePolicyErrorCodes.UnsafeRuntimeConfiguration, adapterResult.SafeMessage);
    }
}
