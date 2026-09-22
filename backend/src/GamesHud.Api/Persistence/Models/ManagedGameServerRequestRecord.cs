namespace GamesHud.Api.Persistence.Models;

public sealed class ManagedGameServerRequestRecord
{
    public string Id { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string GameServerId { get; set; } = string.Empty;
    public string ProvisioningOperationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public ManagedGameServerRecord? GameServer { get; set; }
    public ProvisioningOperationRecord? ProvisioningOperation { get; set; }
}
