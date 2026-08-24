namespace GamesHud.Api.Persistence.Models;

public sealed class StorageReservationRecord
{
    public string Id { get; set; } = string.Empty;

    public string GameServerId { get; set; } = string.Empty;

    public string StorageDefinitionId { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Ownership { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ProvisioningOperationId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ManagedGameServerRecord? GameServer { get; set; }

    public ProvisioningOperationRecord? ProvisioningOperation { get; set; }
}
