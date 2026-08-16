namespace GamesHud.Api.Palworld.Backups.Contracts;

public sealed record PalworldBackupSummaryResponse(
    PalworldBackupScheduleResponse Schedule,
    PalworldBackupStorageResponse Storage,
    PalworldBackupResponse? LatestBackup,
    IReadOnlyCollection<PalworldBackupResponse> Backups);

public sealed record PalworldBackupScheduleResponse(
    bool Enabled,
    int IntervalMinutes,
    int RetentionCount,
    int RetentionDays,
    string? NextScheduledAt);

public sealed record PalworldBackupStorageResponse(
    ulong TotalBytes,
    int BackupCount);

public sealed record PalworldBackupResponse(
    string Id,
    string CreatedAt,
    ulong SizeBytes,
    string Filename,
    string Status,
    string Type,
    string? Note,
    string? WorldSaveStatus,
    string? DownloadUrl);

public sealed record PalworldCreateBackupRequest(
    string? Note);

public sealed record PalworldCreateBackupResponse(
    string Message,
    PalworldBackupResponse Backup);

public sealed record PalworldRestoreBackupRequest(
    string ConfirmationText);

public sealed record PalworldRestoreBackupResponse(
    string Message,
    string RestoredBackupId,
    PalworldBackupResponse PreRestoreBackup,
    int? PlayersOnlineBeforeRestore,
    string StopStatus,
    string StartStatus,
    string HealthCheckStatus,
    string CompletedAt);

public sealed record PalworldDeleteBackupRequest(
    string ConfirmationText);

public sealed record PalworldDeleteBackupResponse(
    string Message,
    string BackupId,
    string DeletedAt);
