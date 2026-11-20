using System.Threading.Channels;
using GamesHud.Api.Persistence.Provisioning;
using GamesHud.Api.Persistence.Models;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Provisioning;

public sealed class ProvisioningExecutorOptions
{
    public const string SectionName = "ProvisioningExecutor";
    public int PollIntervalSeconds { get; set; } = 15;
}

public interface IProvisioningExecutionSignal
{
    void Signal();
    Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken);
}

public sealed class ProvisioningExecutionSignal : IProvisioningExecutionSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Signal() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumDelay);
        try
        {
            if (await _channel.Reader.WaitToReadAsync(timeout.Token))
                while (_channel.Reader.TryRead(out _)) { }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }
}

public sealed class ProvisioningExecutor(
    IServiceScopeFactory scopeFactory,
    IProvisioningExecutionSignal signal,
    IOptions<ProvisioningExecutorOptions> options,
    ILogger<ProvisioningExecutor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Clamp(options.Value.PollIntervalSeconds, 5, 300);
        var interval = TimeSpan.FromSeconds(seconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteEligibleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Managed provisioning discovery failed with exception type {ExceptionType}.",
                    exception.GetType().Name);
            }

            await signal.WaitAsync(interval, stoppingToken);
        }
    }

    private async Task ExecuteEligibleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProvisioningOperationExecutor>()
            .ExecuteEligibleAsync(cancellationToken);
    }
}

public interface IProvisioningOperationExecutor
{
    Task ExecuteEligibleAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(string operationId, CancellationToken cancellationToken);
}

public sealed class ProvisioningOperationExecutor(
    IProvisioningOperationStore operations,
    IProvisioningRecoveryService recovery,
    IProvisioningReconciliationService reconciliation,
    IProvisioningContextLoader contextLoader,
    IProvisioningEngine engine,
    ILogger<ProvisioningOperationExecutor> logger) : IProvisioningOperationExecutor
{
    public async Task ExecuteEligibleAsync(CancellationToken cancellationToken)
    {
        var incomplete = await operations.GetIncompleteAsync(cancellationToken);
        foreach (var operation in incomplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteAsync(operation.OperationId, cancellationToken);
        }
    }

    public async Task ExecuteAsync(string operationId, CancellationToken cancellationToken)
    {
        for (var transition = 0; transition < 32; transition++)
        {
            var decision = await recovery.ClassifyAsync(operationId, cancellationToken);
            if (decision.Decision is ProvisioningRecoveryDecisions.Terminal
                or ProvisioningRecoveryDecisions.ManualIntervention)
            {
                if (decision.Decision == ProvisioningRecoveryDecisions.ManualIntervention)
                    logger.LogWarning("Provisioning operation {OperationId} is blocked: {ReasonCode}",
                        operationId, decision.ReasonCode);
                return;
            }

            if (decision.Decision == ProvisioningRecoveryDecisions.Reconcile)
            {
                var snapshot = await operations.GetAsync(operationId, cancellationToken)
                    ?? throw new ProvisioningTransitionException("Provisioning operation is missing.");
                await reconciliation.ApplyAsync(operationId, decision.StepId!, snapshot.Version, cancellationToken);
                continue;
            }

            var current = await operations.GetAsync(operationId, cancellationToken)
                ?? throw new ProvisioningTransitionException("Provisioning operation is missing.");
            if (current.Steps.All(step => step.Status is ProvisioningStepStatuses.Succeeded
                or ProvisioningStepStatuses.Skipped or ProvisioningStepStatuses.Compensated))
            {
                await operations.FinalizeAsync(operationId, current.Version, cancellationToken);
                return;
            }

            var context = await contextLoader.LoadAsync(operationId, cancellationToken);
            await engine.ExecuteAsync(context, cancellationToken);
            return;
        }

        logger.LogWarning("Provisioning operation {OperationId} exceeded the bounded recovery transition count.", operationId);
    }
}
