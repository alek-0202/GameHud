namespace GamesHud.Api.Palworld.Backups.Services;

public interface IPalworldBackupService
{
    Task<PalworldBackupSummary> GetBackupsAsync(CancellationToken cancellationToken);

    Task<PalworldBackupMetadata?> GetBackupAsync(
        string backupId,
        CancellationToken cancellationToken);

    Task<string> GetBackupFilePathAsync(
        string backupId,
        CancellationToken cancellationToken);

    Task<PalworldBackupMetadata> CreateBackupAsync(
        PalworldBackupCreateOptions options,
        CancellationToken cancellationToken);

    Task<PalworldRestoreBackupResult> RestoreBackupAsync(
        string backupId,
        string confirmationText,
        CancellationToken cancellationToken);

    Task DeleteBackupAsync(
        string backupId,
        string confirmationText,
        CancellationToken cancellationToken);

    Task ApplyRetentionAsync(CancellationToken cancellationToken);
}
