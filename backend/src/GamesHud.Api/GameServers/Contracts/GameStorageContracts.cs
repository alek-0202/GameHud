namespace GamesHud.Api.GameServers.Contracts;

public sealed record GameStoragePlanRequest(
    string GameServerId);

public sealed record GameStoragePlanResponse(
    string GameServerId,
    string GameId,
    string DisplayName,
    string Status,
    string Ownership,
    ulong? RequiredBytes,
    ulong? AvailableBytes,
    IReadOnlyCollection<GameStoragePlanEntryResponse> Entries,
    IReadOnlyCollection<StorageMountResponse> Mounts,
    IReadOnlyCollection<GameStoragePlanIssueResponse> Issues,
    string Message);

public sealed record GameStoragePlanEntryResponse(
    string DefinitionId,
    string Label,
    string Purpose,
    string Ownership,
    string RelativePath,
    string? RuntimeTarget,
    bool Persistent,
    bool Required,
    bool BackupEligible,
    bool UserData,
    ulong? MinimumBytes,
    string Status);

public sealed record StorageMountResponse(
    string StorageDefinitionId,
    string RelativeSource,
    string RuntimeTarget,
    bool ReadOnly);

public sealed record GameStoragePlanIssueResponse(
    string Code,
    string Severity,
    string Message);
