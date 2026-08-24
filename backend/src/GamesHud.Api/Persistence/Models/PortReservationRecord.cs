namespace GamesHud.Api.Persistence.Models;

public static class ReservationStatuses
{
    public const string Reserved = "reserved";
}

public sealed class PortReservationRecord
{
    public string Id { get; set; } = string.Empty;

    public string GameServerId { get; set; } = string.Empty;

    public string PortDefinitionId { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Exposure { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ProvisioningOperationId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ManagedGameServerRecord? GameServer { get; set; }

    public ProvisioningOperationRecord? ProvisioningOperation { get; set; }
}
