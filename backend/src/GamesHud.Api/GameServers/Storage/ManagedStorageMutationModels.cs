using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Storage;

public static class ManagedStorageErrorCodes
{
    public const string TargetInvalid = "storage_target_unsafe";
    public const string OwnershipInvalid = "storage_ownership_invalid";
    public const string PrepareFailed = "storage_prepare_failed";
    public const string AccessDenied = "storage_access_denied";
    public const string ReconciliationAmbiguous = "storage_reconciliation_ambiguous";
}

public sealed record ManagedStorageTargetEntry(
    string ReservationId,
    string StorageDefinitionId,
    string RelativePath,
    string AbsolutePath);

public sealed class ValidatedManagedStorageTarget
{
    internal ValidatedManagedStorageTarget(
        GameServerId gameServerId,
        string operationId,
        string dataRoot,
        IReadOnlyCollection<ManagedStorageTargetEntry> entries)
    {
        GameServerId = gameServerId;
        OperationId = operationId;
        DataRoot = dataRoot;
        Entries = entries;
    }

    public GameServerId GameServerId { get; }
    public string OperationId { get; }
    internal string DataRoot { get; }
    public IReadOnlyCollection<ManagedStorageTargetEntry> Entries { get; }
}

public sealed record ManagedStorageTargetBuildResult(
    ValidatedManagedStorageTarget? Target,
    string? SafeErrorCode,
    string? SafeMessage)
{
    public bool Succeeded => Target is not null;
}
