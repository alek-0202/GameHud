namespace GamesHud.Api.Persistence.Models;

public static class ProvisioningOperationTypes
{
    public const string Provision = "provision";
}

public static class ProvisioningOperationStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class ProvisioningOperationActiveSlots
{
    public const string Active = "active";
}

public sealed class ProvisioningOperationRecord
{
    public string Id { get; set; } = string.Empty;

    public string GameServerId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? ActiveSlot { get; set; }

    public string CurrentStep { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessageSafe { get; set; }

    public ManagedGameServerRecord? GameServer { get; set; }

    public ICollection<PortReservationRecord> PortReservations { get; } = [];

    public ICollection<StorageReservationRecord> StorageReservations { get; } = [];
}
