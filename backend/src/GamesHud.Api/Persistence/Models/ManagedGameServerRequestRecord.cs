namespace GamesHud.Api.Persistence.Models;

using GamesHud.Api.Authentication;

public sealed class ManagedGameServerRequestRecord
{
    public string Id { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string GameServerId { get; set; } = string.Empty;
    public string ProvisioningOperationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public ManagedGameServerRecord? GameServer { get; set; }
    public ProvisioningOperationRecord? ProvisioningOperation { get; set; }
}
