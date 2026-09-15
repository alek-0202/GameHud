namespace GamesHud.Api.Persistence.Models;

public sealed class RuntimeImageIntentRecord
{
    public string OperationId { get; set; } = string.Empty;
    public string GameServerId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string ApprovedDigest { get; set; } = string.Empty;
    public string PlatformOs { get; set; } = string.Empty;
    public string PlatformArchitecture { get; set; } = string.Empty;
    public string? PlatformVariant { get; set; }
    public string ApprovalSource { get; set; } = string.Empty;
    public string? VerifiedLocalImageId { get; set; }
    public string VerificationState { get; set; } = "pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public int Version { get; set; } = 1;
}
