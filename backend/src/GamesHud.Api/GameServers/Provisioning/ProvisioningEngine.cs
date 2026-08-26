using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;

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
    private readonly IManagedServerStore _store;
    private readonly IReadOnlyDictionary<string, IProvisioningStep> _steps;
    private readonly ILogger<ProvisioningEngine> _logger;

    public ProvisioningEngine(
        IManagedServerStore store,
        IEnumerable<IProvisioningStep> steps,
        ILogger<ProvisioningEngine> logger)
    {
        _store = store;
        _steps = steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        _logger = logger;
    }

    public async Task<ProvisioningExecutionResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var completed = new List<IProvisioningStep>();

        foreach (var stepId in ProvisioningStepIds.ExecutableFoundation)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _store.UpdateOperationAsync(
                    new ProvisioningOperationUpdate(
                        context.OperationId,
                        ProvisioningOperationStatuses.Running,
                        stepId),
                    cancellationToken);

                var step = _steps[stepId];
                var result = await step.ExecuteAsync(context, cancellationToken);
                context.Record(stepId, result.Status);
                _logger.LogInformation(
                    "Provisioning operation {OperationId} for server {GameServerId} completed step {StepId} with {Result}",
                    context.OperationId, context.GameServerId, stepId, result.Status);

                if (result.Status == ProvisioningStepResultStatuses.Failed)
                {
                    await CompensateAsync(completed, context);
                    return await FailAsync(
                        context,
                        stepId,
                        result.ErrorCode ?? ProvisioningErrorCodes.StepFailed,
                        result.SafeMessage ?? "A provisioning step failed.");
                }

                completed.Add(step);
            }
            catch (OperationCanceledException)
            {
                await CompensateAsync(completed, context);
                return await FailAsync(
                    context,
                    stepId,
                    ProvisioningErrorCodes.ProvisioningCancelled,
                    "Provisioning was cancelled.");
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Provisioning operation {OperationId} for server {GameServerId} failed at step {StepId} with exception type {ExceptionType}",
                    context.OperationId, context.GameServerId, stepId, exception.GetType().Name);
                await CompensateAsync(completed, context);
                return await FailAsync(
                    context,
                    stepId,
                    ProvisioningErrorCodes.StepFailed,
                    "An unexpected provisioning error occurred.");
            }
        }

        await _store.UpdateOperationAsync(
            new ProvisioningOperationUpdate(
                context.OperationId,
                ProvisioningOperationStatuses.Succeeded,
                ProvisioningStepIds.Complete),
            CancellationToken.None);

        return new ProvisioningExecutionResult(
            true,
            context.OperationId,
            ProvisioningOperationStatuses.Succeeded,
            null);
    }

    private async Task<ProvisioningExecutionResult> FailAsync(
        ProvisioningContext context,
        string stepId,
        string code,
        string message)
    {
        await _store.UpdateOperationAsync(
            new ProvisioningOperationUpdate(
                context.OperationId,
                ProvisioningOperationStatuses.Failed,
                stepId,
                code,
                message),
            CancellationToken.None);

        return new ProvisioningExecutionResult(
            false,
            context.OperationId,
            ProvisioningOperationStatuses.Failed,
            new ProvisioningFailure(code, message));
    }

    private static async Task CompensateAsync(
        IEnumerable<IProvisioningStep> completed,
        ProvisioningContext context)
    {
        foreach (var step in completed.Reverse().OfType<ICompensatingProvisioningStep>())
        {
            try
            {
                await step.CompensateAsync(context, CancellationToken.None);
            }
            catch
            {
                // GH-15 will introduce durable compensation outcomes and retry policy.
            }
        }
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
            : ProvisioningStepResult.Skipped("Host mutation is not implemented in GH-08."));
    }
}
