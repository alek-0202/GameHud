namespace GamesHud.Api.Persistence.Models;

public static class ManagedInstallationTypes
{
    public const string Managed = "managed";
    public const string External = "external";
}

public static class ManagedGameServerLifecycleStates
{
    public const string PendingProvisioning = "pending_provisioning";
}

public sealed class ManagedGameServerRecord
{
    public string Id { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string InstallationType { get; set; } = string.Empty;

    public string RuntimeType { get; set; } = string.Empty;

    public string LifecycleState { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<PortReservationRecord> PortReservations { get; } = [];

    public ICollection<StorageReservationRecord> StorageReservations { get; } = [];

    public ICollection<ProvisioningOperationRecord> ProvisioningOperations { get; } = [];
}
