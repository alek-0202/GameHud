using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.GameServers.Provisioning;

public interface IProvisioningStateMachine
{
    void EnsureOperationTransition(string current, string next, bool explicitRetry = false);
    void EnsureStepTransition(ProvisioningStepSnapshot step, string next, bool explicitRetry = false);
}

public sealed class ProvisioningStateMachine : IProvisioningStateMachine
{
    public void EnsureOperationTransition(string current, string next, bool explicitRetry = false)
    {
        var valid = (current, next) switch
        {
            (ProvisioningOperationStatuses.Pending, ProvisioningOperationStatuses.Running) => true,
            (ProvisioningOperationStatuses.Pending, ProvisioningOperationStatuses.Cancelled) => true,
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Running) => true,
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Succeeded) => true,
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Failed) => true,
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Cancelled) => true,
            (ProvisioningOperationStatuses.Running, ProvisioningOperationStatuses.Compensating) => true,
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Compensating) => true,
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Failed) => true,
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.Cancelled) => true,
            (ProvisioningOperationStatuses.Compensating, ProvisioningOperationStatuses.CompensationFailed) => true,
            (ProvisioningOperationStatuses.Failed, ProvisioningOperationStatuses.Running) => explicitRetry,
            (ProvisioningOperationStatuses.CompensationFailed, ProvisioningOperationStatuses.Compensating) => explicitRetry,
            _ => false
        };

        if (!valid)
        {
            throw new ProvisioningTransitionException(
                $"Provisioning operation cannot transition from '{current}' to '{next}'.");
        }
    }

    public void EnsureStepTransition(ProvisioningStepSnapshot step, string next, bool explicitRetry = false)
    {
        var valid = (step.Status, next) switch
        {
            (ProvisioningStepStatuses.Pending, ProvisioningStepStatuses.Running) => true,
            (ProvisioningStepStatuses.Pending, ProvisioningStepStatuses.Skipped) => true,
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Succeeded) => true,
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Failed) => true,
            (ProvisioningStepStatuses.Running, ProvisioningStepStatuses.Skipped) => true,
            (ProvisioningStepStatuses.Failed, ProvisioningStepStatuses.Running) =>
                explicitRetry
                && step.RetryClassification == ProvisioningRetryClassifications.SafeToRetry
                && step.Attempt < step.MaxAttempts,
            (ProvisioningStepStatuses.Succeeded, ProvisioningStepStatuses.Compensating) => true,
            (ProvisioningStepStatuses.Compensating, ProvisioningStepStatuses.Compensated) => true,
            (ProvisioningStepStatuses.Compensating, ProvisioningStepStatuses.CompensationFailed) => true,
            (ProvisioningStepStatuses.CompensationFailed, ProvisioningStepStatuses.Compensating) => explicitRetry,
            _ => false
        };

        if (!valid)
        {
            throw new ProvisioningTransitionException(
                $"Provisioning step '{step.StepId}' cannot transition from '{step.Status}' to '{next}'.");
        }
    }
}

public sealed class ProvisioningTransitionException : InvalidOperationException
{
    public ProvisioningTransitionException(string message) : base(message)
    {
    }
}
