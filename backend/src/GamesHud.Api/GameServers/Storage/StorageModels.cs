using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Storage;

public static class StoragePlanStatuses
{
    public const string Ready = "ready";
    public const string Warning = "warning";
    public const string Insufficient = "insufficient";
    public const string Collision = "collision";
    public const string Unknown = "unknown";
}

public static class StorageIssueCodes
{
    public const string InvalidGameServerId = "invalid_game_server_id";
    public const string ExistingPath = "existing_path";
    public const string InsufficientStorage = "insufficient_storage";
    public const string StorageUnknown = "storage_unknown";
}

public sealed record StorageMount(
    string StorageDefinitionId,
    string SourcePath,
    string RuntimeTarget,
    bool ReadOnly);

public sealed record GameStoragePlan(
    GameServerId GameServerId,
    string GameId,
    string DisplayName,
    string Status,
    string DataRoot,
    string ServerRoot,
    string ServerRelativePath,
    string Ownership,
    ulong? RequiredBytes,
    ulong? AvailableBytes,
    IReadOnlyCollection<GameStoragePlanEntry> Entries,
    IReadOnlyCollection<StorageMount> Mounts,
    IReadOnlyCollection<GameStoragePlanIssue> Issues,
    string Message);

public sealed record GameStoragePlanEntry(
    string DefinitionId,
    string Label,
    string Purpose,
    string Ownership,
    string HostPath,
    string RelativePath,
    string? RuntimeTarget,
    bool Persistent,
    bool Required,
    bool BackupEligible,
    bool UserData,
    ulong? MinimumBytes,
    string Status);

public sealed record GameStoragePlanIssue(
    string Code,
    string Severity,
    string Message);

public sealed class StoragePlanningException : Exception
{
    public StoragePlanningException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
