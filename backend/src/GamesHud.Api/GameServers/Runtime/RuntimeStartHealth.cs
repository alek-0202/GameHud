using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Runtime;

internal static class RuntimeStartHealthErrorCodes
{
    public const string SpecificationInvalid = "runtime_specification_invalid";
    public const string HealthTimeout = "runtime_health_timeout";
    public const string Unhealthy = "runtime_unhealthy";
    public const string HealthUnknown = "runtime_health_unknown";
}

internal sealed class StartRuntimeProvisioningStep : IProvisioningStep, ICompensatingProvisioningStep
{
    private readonly IRuntimeSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly IRuntimeMutationExecutor _executor;

    public StartRuntimeProvisioningStep(IRuntimeSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IManagedStoragePathBuilder paths, IRuntimeMutationExecutor executor)
    {
        _builder = builder;
        _policy = policy;
        _paths = paths;
        _executor = executor;
    }

    public string Id => ProvisioningStepIds.StartRuntime;

    public async Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var specification = await _builder.BuildAsync(context, cancellationToken);
        if (specification is null) return Invalid();
        var validated = _policy.Validate(specification, context.GameDefinition, _paths.CreateLayout(context.GameServerId).DataRoot);
        if (!validated.Allowed) return Invalid();
        var execution = await _executor.ExecuteStartAsync(new RuntimeMutationExecutionContext(validated.Specification!,
            RuntimeMutationKind.StartRuntime, Id, 1), cancellationToken);
        return execution.Status switch
        {
            RuntimeMutationOutcomeStatuses.Success => ProvisioningStepResult.Success(),
            RuntimeMutationOutcomeStatuses.CancelledBeforeInvocation => throw new OperationCanceledException(cancellationToken),
            RuntimeMutationOutcomeStatuses.KnownFailure => ProvisioningStepResult.Failure(execution.SafeCode!, execution.SafeMessage!, ProvisioningFailureTypes.Transient),
            _ => ProvisioningStepResult.Failure(execution.SafeCode ?? DockerRuntimeErrorCodes.StartUnknown,
                execution.SafeMessage ?? "Managed runtime start outcome is unknown.", ProvisioningFailureTypes.Unknown)
        };
    }

    public Task CompensateAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static ProvisioningStepResult Invalid() => ProvisioningStepResult.Failure(
        RuntimeStartHealthErrorCodes.SpecificationInvalid, "Managed runtime identity could not be validated.");
}

internal sealed class StartRuntimeReconciler : IProvisioningStepReconciler
{
    private readonly IRuntimeReconciliationSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly IManagedRuntimeInspector _inspector;

    public StartRuntimeReconciler(IRuntimeReconciliationSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IManagedStoragePathBuilder paths, IManagedRuntimeInspector inspector)
    {
        _builder = builder;
        _policy = policy;
        _paths = paths;
        _inspector = inspector;
    }

    public string StepId => ProvisioningStepIds.StartRuntime;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation, ProvisioningStepSnapshot step, CancellationToken cancellationToken)
    {
        var context = await BuildAsync(operation.OperationId, operation.GameServerId, Math.Max(step.Attempt, 1), cancellationToken);
        if (context is null) return Ambiguous();
        try
        {
            var inspection = await _inspector.InspectManagedAsync(context, cancellationToken);
            return inspection.State switch
            {
                ManagedRuntimeStates.Running => new(ProvisioningReconciliationOutcomes.EffectExists, "Managed runtime is running."),
                ManagedRuntimeStates.Created or ManagedRuntimeStates.Exited => new(ProvisioningReconciliationOutcomes.EffectAbsent, "Managed runtime is stopped."),
                _ => Ambiguous()
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Ambiguous(); }
    }

    private async Task<RuntimeMutationExecutionContext?> BuildAsync(string operationId, string serverId, int attempt,
        CancellationToken cancellationToken)
    {
        var id = new GameServerId(serverId);
        var built = await _builder.BuildForReconciliationAsync(operationId, id, cancellationToken);
        if (built.Specification is null || built.Definition is null) return null;
        var validated = _policy.Validate(built.Specification, built.Definition, _paths.CreateLayout(id).DataRoot);
        return validated.Allowed
            ? new(validated.Specification!, RuntimeMutationKind.StartRuntime, StepId, attempt)
            : null;
    }

    private static ProvisioningReconciliationResult Ambiguous() => new(
        ProvisioningReconciliationOutcomes.Ambiguous, "Managed runtime start state could not be proven safely.");
}

internal interface IRuntimeHealthDelay
{
    Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken);
}

internal sealed class RuntimeHealthDelay : IRuntimeHealthDelay
{
    public Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken) => Task.Delay(interval, cancellationToken);
}

internal sealed class VerifyRuntimeHealthProvisioningStep : IProvisioningStep
{
    private readonly IRuntimeSpecificationBuilder _builder;
    private readonly IRuntimeMutationPolicy _policy;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly IManagedRuntimeInspector _inspector;
    private readonly IRuntimeHealthDelay _delay;
    private readonly RuntimeHealthOptions _options;

    public VerifyRuntimeHealthProvisioningStep(IRuntimeSpecificationBuilder builder, IRuntimeMutationPolicy policy,
        IManagedStoragePathBuilder paths, IManagedRuntimeInspector inspector, IRuntimeHealthDelay delay,
        IOptions<RuntimeHealthOptions> options)
    {
        _builder = builder;
        _policy = policy;
        _paths = paths;
        _inspector = inspector;
        _delay = delay;
        _options = options.Value;
    }

    public string Id => ProvisioningStepIds.VerifyHealth;

    public async Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        if (_options.TimeoutSeconds <= 0 || _options.TimeoutSeconds > RuntimeHealthOptions.MaximumTimeoutSeconds
            || _options.PollIntervalSeconds <= 0 || _options.PollIntervalSeconds > RuntimeHealthOptions.MaximumPollIntervalSeconds
            || _options.PollIntervalSeconds > _options.TimeoutSeconds)
            return ProvisioningStepResult.Failure(RuntimeStartHealthErrorCodes.HealthUnknown, "Runtime health configuration is invalid.");
        var specification = await _builder.BuildAsync(context, cancellationToken);
        if (specification is null) return Invalid();
        var validated = _policy.Validate(specification, context.GameDefinition, _paths.CreateLayout(context.GameServerId).DataRoot);
        if (!validated.Allowed) return Invalid();
        var executionContext = new RuntimeMutationExecutionContext(validated.Specification!, RuntimeMutationKind.StartRuntime, Id, 1);
        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        var attempts = checked((_options.TimeoutSeconds + _options.PollIntervalSeconds - 1) / _options.PollIntervalSeconds + 1);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ManagedRuntimeInspection inspection;
            try { inspection = await _inspector.InspectManagedAsync(executionContext, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return ProvisioningStepResult.Failure(RuntimeStartHealthErrorCodes.HealthUnknown,
                    "Managed runtime readiness could not be inspected.", ProvisioningFailureTypes.Transient);
            }
            if (inspection.State == ManagedRuntimeStates.Running)
            {
                if (inspection.DockerHealth == "unhealthy")
                    return ProvisioningStepResult.Failure(RuntimeStartHealthErrorCodes.Unhealthy, "Managed runtime health check is unhealthy.");
                if (inspection.DockerHealth is null or "" or "healthy") return ProvisioningStepResult.Success();
            }
            else if (inspection.State is ManagedRuntimeStates.Dead or ManagedRuntimeStates.Absent)
                return ProvisioningStepResult.Failure(RuntimeStartHealthErrorCodes.Unhealthy, "Managed runtime is not healthy.");

            if (attempt + 1 < attempts) await _delay.WaitAsync(interval, cancellationToken);
        }
        return ProvisioningStepResult.Failure(RuntimeStartHealthErrorCodes.HealthTimeout,
            "Managed runtime did not become ready within the configured timeout.", ProvisioningFailureTypes.Transient);
    }

    private static ProvisioningStepResult Invalid() => ProvisioningStepResult.Failure(
        RuntimeStartHealthErrorCodes.SpecificationInvalid, "Managed runtime identity could not be validated.");
}
