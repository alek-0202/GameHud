using GamesHud.Api.Palworld.Backups.Contracts;

namespace GamesHud.Api.Palworld.Backups.Services;

public static class PalworldBackupContractMapper
{
    public static PalworldBackupSummaryResponse Map(PalworldBackupSummary summary)
    {
        return new PalworldBackupSummaryResponse(
            new PalworldBackupScheduleResponse(
                summary.Schedule.Enabled,
                summary.Schedule.IntervalMinutes,
                summary.Schedule.RetentionCount,
                summary.Schedule.RetentionDays,
                summary.Schedule.NextScheduledAt?.ToString("O")),
            new PalworldBackupStorageResponse(
                summary.Storage.TotalBytes,
                summary.Storage.BackupCount),
            summary.LatestBackup is null ? null : Map(summary.LatestBackup),
            summary.Backups.Select(Map).ToArray());
    }

    public static PalworldBackupResponse Map(PalworldBackupMetadata backup)
    {
        return new PalworldBackupResponse(
            backup.Id,
            backup.CreatedAt.ToString("O"),
            backup.SizeBytes,
            backup.Filename,
            backup.Status,
            backup.Type,
            backup.Note,
            backup.WorldSaveStatus,
            $"/api/palworld/backups/{backup.Id}/download");
    }
}
