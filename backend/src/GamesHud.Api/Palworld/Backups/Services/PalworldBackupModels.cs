namespace GamesHud.Api.Palworld.Backups.Services;

public static class PalworldBackupTypes
{
    public const string Manual = "manual";
    public const string Automatic = "automatic";
    public const string PreRestore = "pre-restore";
    public const string PreUpdate = "pre-update";
}

public static class PalworldBackupStatuses
{
    public const string Completed = "completed";
}

public sealed record PalworldBackupMetadata(
    string Id,
    DateTimeOffset CreatedAt,
    ulong SizeBytes,
    string Filename,
    string Status,
    string Type,
    string? Note,
    string? WorldSaveStatus);

public sealed record PalworldBackupSummary(
    PalworldBackupSchedule Schedule,
    PalworldBackupStorage Storage,
    PalworldBackupMetadata? LatestBackup,
    IReadOnlyCollection<PalworldBackupMetadata> Backups);

public sealed record PalworldBackupSchedule(
    bool Enabled,
    int IntervalMinutes,
    int RetentionCount,
    int RetentionDays,
    DateTimeOffset? NextScheduledAt);

public sealed record PalworldBackupStorage(
    ulong TotalBytes,
    int BackupCount);

public sealed record PalworldBackupCreateOptions(
    string Type,
    string? Note,
    bool RequestWorldSave);

public sealed record PalworldRestoreBackupResult(
    string RestoredBackupId,
    PalworldBackupMetadata PreRestoreBackup,
    int? PlayersOnlineBeforeRestore,
    string StopStatus,
    string StartStatus,
    string HealthCheckStatus,
    DateTimeOffset CompletedAt);
