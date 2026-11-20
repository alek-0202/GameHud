namespace GamesHud.Api.Persistence.Models;

public sealed class ProvisioningReconciliationRecord
{
    public string Id { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public int OperationVersion { get; set; }
    public int Attempt { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string PriorStatus { get; set; } = string.Empty;
    public string? PriorFailureType { get; set; }
    public string? PriorErrorCode { get; set; }
    public string AppliedCode { get; set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; set; }
}
