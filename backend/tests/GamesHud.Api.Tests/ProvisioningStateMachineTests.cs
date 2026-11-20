using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.Tests;

public sealed class ProvisioningStateMachineTests
{
    private static readonly string[] OperationStates =
    [
        ProvisioningOperationStatuses.Pending,
        ProvisioningOperationStatuses.Running,
        ProvisioningOperationStatuses.Succeeded,
        ProvisioningOperationStatuses.Failed,
        ProvisioningOperationStatuses.Cancelled,
        ProvisioningOperationStatuses.Compensating,
        ProvisioningOperationStatuses.CompensationFailed
    ];

    private static readonly string[] StepStates =
    [
        ProvisioningStepStatuses.Pending,
        ProvisioningStepStatuses.Running,
        ProvisioningStepStatuses.Succeeded,
        ProvisioningStepStatuses.Failed,
        ProvisioningStepStatuses.Skipped,
        ProvisioningStepStatuses.Compensating,
        ProvisioningStepStatuses.Compensated,
        ProvisioningStepStatuses.CompensationFailed
    ];

    [Fact]
    public void OperationTransitionMatrixIsExplicitAndExhaustive()
    {
        var valid = new HashSet<(string, string)>
        {
            (ProvisioningOperationStatuses.Pending, ProvisioningOperationStatuses.Running),
            (ProvisioningOperationStatuses.Pending, ProvisioningOperationStatuses.Cancelled),
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Running),
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Succeeded),
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Failed),
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Cancelled),
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Compensating),
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Compensating),
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Failed),
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Cancelled),
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.CompensationFailed)
        };
        var machine = new ProvisioningStateMachine();

        foreach (var current in OperationStates)
        {
            foreach (var next in OperationStates)
            {
                if (valid.Contains((current, next)))
                {
                    machine.EnsureOperationTransition(current, next);
                }
                else
                {
                    Assert.Throws<ProvisioningTransitionException>(() =>
                        machine.EnsureOperationTransition(current, next));
                }
            }
        }
    }

    [Fact]
    public void StepTransitionMatrixIsExplicitAndExhaustive()
    {
        var valid = new HashSet<(string, string)>
        {
            (ProvisioningStepStatuses.Pending, ProvisioningStepStatuses.Running),
            (ProvisioningStepStatuses.Pending, ProvisioningStepStatuses.Skipped),
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Succeeded),
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Failed),
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Skipped),
            (ProvisioningStepStatuses.Succeeded, ProvisioningStepStatuses.Compensating),
            (ProvisioningStepStatuses.Compensating, ProvisioningStepStatuses.Compensated),
            (ProvisioningStepStatuses.Compensating, ProvisioningStepStatuses.CompensationFailed)
        };
        var machine = new ProvisioningStateMachine();

        foreach (var current in StepStates)
        {
            foreach (var next in StepStates)
            {
                var step = CreateStep(current);
                if (valid.Contains((current, next)))
                {
                    machine.EnsureStepTransition(step, next);
                }
                else
                {
                    Assert.Throws<ProvisioningTransitionException>(() =>
                        machine.EnsureStepTransition(step, next));
                }
            }
        }
    }

    [Fact]
    public void ExplicitRetryRequiresSafeClassificationAndRemainingAttempt()
    {
        var machine = new ProvisioningStateMachine();
        var safe = CreateStep(
            ProvisioningStepStatuses.Failed,
            ProvisioningRetryClassifications.SafeToRetry,
            attempt: 1,
            maxAttempts: 2);
        var exhausted = safe with { Attempt = 2 };
        var mutation = safe with { RetryClassification = ProvisioningRetryClassifications.RequiresInspection };

        machine.EnsureStepTransition(safe, ProvisioningStepStatuses.Running, explicitRetry: true);
        Assert.Throws<ProvisioningTransitionException>(() =>
            machine.EnsureStepTransition(exhausted, ProvisioningStepStatuses.Running, explicitRetry: true));
        Assert.Throws<ProvisioningTransitionException>(() =>
            machine.EnsureStepTransition(mutation, ProvisioningStepStatuses.Running, explicitRetry: true));
        Assert.Throws<ProvisioningTransitionException>(() =>
            machine.EnsureStepTransition(safe, ProvisioningStepStatuses.Running));
    }

    [Fact]
    public void FailedOperationCanRunOnlyThroughExplicitRetry()
    {
        var machine = new ProvisioningStateMachine();

        machine.EnsureOperationTransition(
            ProvisioningOperationStatuses.Failed,
            ProvisioningOperationStatuses.Running,
            explicitRetry: true);
        Assert.Throws<ProvisioningTransitionException>(() => machine.EnsureOperationTransition(
            ProvisioningOperationStatuses.Failed,
            ProvisioningOperationStatuses.Running));
    }

    [Fact]
    public void FailedCompensationCanResumeOnlyThroughExplicitRetry()
    {
        var machine = new ProvisioningStateMachine();
        var step = CreateStep(ProvisioningStepStatuses.CompensationFailed);

        machine.EnsureOperationTransition(
            ProvisioningOperationStatuses.CompensationFailed,
            ProvisioningOperationStatuses.Compensating,
            explicitRetry: true);
        machine.EnsureStepTransition(step, ProvisioningStepStatuses.Compensating, explicitRetry: true);
        Assert.Throws<ProvisioningTransitionException>(() => machine.EnsureOperationTransition(
            ProvisioningOperationStatuses.CompensationFailed,
            ProvisioningOperationStatuses.Compensating));
        Assert.Throws<ProvisioningTransitionException>(() => machine.EnsureStepTransition(
            step,
            ProvisioningStepStatuses.Compensating));
    }

    private static ProvisioningStepSnapshot CreateStep(
        string status,
        string retry = ProvisioningRetryClassifications.SafeToRetry,
        int attempt = 0,
        int maxAttempts = 2) =>
        new(
            ProvisioningStepIds.VerifyHealth,
            8,
            status,
            attempt,
            retry,
            ProvisioningSideEffectClassifications.ReadOnly,
            maxAttempts,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
}
