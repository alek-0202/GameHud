namespace GamesHud.Api.Persistence.Models;

public static class ProvisioningStepStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Compensating = "compensating";
    public const string Compensated = "compensated";
    public const string CompensationFailed = "compensation_failed";
}

public sealed class ProvisioningStepRecord
{
    public string Id { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public string RetryClassification { get; set; } = string.Empty;
    public string SideEffectClassification { get; set; } = string.Empty;
    public int MaxAttempts { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureType { get; set; }
    public string? ErrorCode { get; set; }
    public string? SafeErrorMessage { get; set; }
    public DateTimeOffset? CompensationStartedAtUtc { get; set; }
    public DateTimeOffset? CompensationCompletedAtUtc { get; set; }
    public ProvisioningOperationRecord? Operation { get; set; }
}
