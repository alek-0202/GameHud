using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;

namespace GamesHud.Api.GameServers.Storage;

public sealed class PrepareStorageProvisioningStep : IProvisioningStep, ICompensatingProvisioningStep
{
    private readonly IManagedStorageTargetBuilder _builder;
    private readonly IManagedStorageProvider _provider;
    private readonly ILogger<PrepareStorageProvisioningStep> _logger;

    public PrepareStorageProvisioningStep(IManagedStorageTargetBuilder builder, IManagedStorageProvider provider,
        ILogger<PrepareStorageProvisioningStep> logger)
    {
        _builder = builder;
        _provider = provider;
        _logger = logger;
    }

    public string Id => ProvisioningStepIds.PrepareStorage;

    public async Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var built = await _builder.BuildAsync(context, cancellationToken);
        if (!built.Succeeded)
            return ProvisioningStepResult.Failure(built.SafeErrorCode!, built.SafeMessage!);

        var outcome = await _provider.PrepareAsync(built.Target!, cancellationToken);
        _logger.LogInformation(
            "Managed storage mutation for operation {OperationId}, server {GameServerId}, mutation {MutationKind} completed with {Outcome} and safe code {SafeErrorCode}",
            context.OperationId, context.GameServerId, "prepare_storage", outcome.Status, outcome.SafeCode);
        return outcome.Status switch
        {
            RuntimeMutationOutcomeStatuses.Success => ProvisioningStepResult.Success(),
            RuntimeMutationOutcomeStatuses.KnownFailure => ProvisioningStepResult.Failure(
                outcome.SafeCode ?? ManagedStorageErrorCodes.PrepareFailed,
                outcome.SafeMessage ?? "Managed storage could not be prepared."),
            _ => ProvisioningStepResult.Failure(
                outcome.SafeCode ?? ManagedStorageErrorCodes.ReconciliationAmbiguous,
                outcome.SafeMessage ?? "Managed storage preparation outcome is unknown.", ProvisioningFailureTypes.Unknown)
        };
    }

    public Task CompensateAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class PrepareStorageReconciler : IProvisioningStepReconciler
{
    private readonly IManagedStorageTargetBuilder _builder;
    private readonly IManagedStorageProvider _provider;

    public PrepareStorageReconciler(IManagedStorageTargetBuilder builder, IManagedStorageProvider provider)
    {
        _builder = builder;
        _provider = provider;
    }

    public string StepId => ProvisioningStepIds.PrepareStorage;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation, ProvisioningStepSnapshot step, CancellationToken cancellationToken)
    {
        var built = await _builder.BuildForReconciliationAsync(
            operation.OperationId, new GamesHud.Api.GameServers.Domain.GameServerId(operation.GameServerId), cancellationToken);
        return built.Succeeded
            ? await _provider.InspectAsync(built.Target!, cancellationToken)
            : new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed storage state could not be proven safely.");
    }

}
