using GamesHud.Api.GameServers.Provisioning;

namespace GamesHud.Api.Persistence.Provisioning;

public sealed record ProvisioningCheckpoint(
    string OperationId,
    int ExpectedVersion,
    string OperationStatus,
    string CurrentStep,
    string? StepId = null,
    string? StepStatus = null,
    string? FailureType = null,
    string? ErrorCode = null,
    string? SafeErrorMessage = null,
    bool ExplicitRetry = false,
    bool KeepActiveSlot = false);

public interface IProvisioningOperationStore
{
    Task<ProvisioningOperationSnapshot?> GetAsync(string operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteAsync(CancellationToken cancellationToken = default);
    Task<ProvisioningOperationSnapshot> ApplyCheckpointAsync(ProvisioningCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

public sealed class ProvisioningConcurrencyException : InvalidOperationException
{
    public ProvisioningConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
