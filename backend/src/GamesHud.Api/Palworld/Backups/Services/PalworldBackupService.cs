using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Backups.Services;

public sealed class PalworldBackupService : IPalworldBackupService
{
    private const string BackupExtension = ".tar.gz";
    private const string MetadataExtension = ".json";
    private const int MaxNoteLength = 256;
    private const string RestoreConfirmation = "RESTORE PALWORLD BACKUP";
    private const string DeleteConfirmation = "DELETE PALWORLD BACKUP";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IOptions<PalworldOptions> _options;
    private readonly IPalworldRestService _palworldRestService;
    private readonly IContainerService _containerService;
    private readonly INotificationService _notificationService;
    private readonly PalworldBackupScheduleState _scheduleState;
    private readonly ILogger<PalworldBackupService> _logger;

    public PalworldBackupService(
        IOptions<PalworldOptions> options,
        IPalworldRestService palworldRestService,
        IContainerService containerService,
        INotificationService notificationService,
        PalworldBackupScheduleState scheduleState,
        ILogger<PalworldBackupService> logger)
    {
        _options = options;
        _palworldRestService = palworldRestService;
        _containerService = containerService;
        _notificationService = notificationService;
        _scheduleState = scheduleState;
        _logger = logger;
    }

    public async Task<PalworldBackupSummary> GetBackupsAsync(CancellationToken cancellationToken)
    {
        var context = ResolveContext();
        var backups = await ReadAllMetadataAsync(context.BackupPath, cancellationToken);
        var completedBackups = backups
            .Where(IsCompleted)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
        var totalBytes = completedBackups.Aggregate(0UL, (total, backup) => total + backup.SizeBytes);

        return new PalworldBackupSummary(
            CreateSchedule(),
            new PalworldBackupStorage(totalBytes, completedBackups.Length),
            completedBackups.FirstOrDefault(),
            completedBackups);
    }

    public async Task<PalworldBackupMetadata?> GetBackupAsync(
        string backupId,
        CancellationToken cancellationToken)
    {
        var context = ResolveContext();
        var safeBackupId = ValidateBackupId(backupId);
        var metadataPath = GetMetadataPath(context.BackupPath, safeBackupId);

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        return await ReadMetadataAsync(metadataPath, cancellationToken);
    }

    public async Task<string> GetBackupFilePathAsync(
        string backupId,
        CancellationToken cancellationToken)
    {
        var context = ResolveContext();
        var metadata = await GetRequiredBackupAsync(context.BackupPath, backupId, cancellationToken);
        var archivePath = GetArchivePath(context.BackupPath, metadata.Id);

        if (!File.Exists(archivePath))
        {
            throw new PalworldBackupNotFoundException("Backup archive was not found.");
        }

        return archivePath;
    }

    public async Task<PalworldBackupMetadata> CreateBackupAsync(
        PalworldBackupCreateOptions options,
        CancellationToken cancellationToken)
    {
        ValidateCreateOptions(options);
        var context = ResolveContext();
        var worldSaveStatus = await TryRequestWorldSaveAsync(
            options.RequestWorldSave,
            cancellationToken);
        PalworldBackupMetadata metadata;

        try
        {
            metadata = await CreateArchiveAsync(
                context,
                options.Type,
                options.Note,
                worldSaveStatus,
                cancellationToken);
        }
        catch
        {
            await _notificationService.NotifyAsync(
                new NotificationEvent(
                    NotificationEventTypes.BackupFailed,
                    "Palworld backup failed",
                    "Palworld backup creation failed.",
                    "palworld-backup-failed"),
                cancellationToken);
            throw;
        }

        if (options.Type.Equals(PalworldBackupTypes.Automatic, StringComparison.Ordinal))
        {
            await ApplyRetentionAsync(cancellationToken);
        }

        await _notificationService.NotifyAsync(
            new NotificationEvent(
                NotificationEventTypes.BackupCompleted,
                "Palworld backup completed",
                $"Palworld backup {metadata.Type} completed.",
                $"palworld-backup-completed-{metadata.Type}"),
            cancellationToken);

        return metadata;
    }

    public async Task<PalworldRestoreBackupResult> RestoreBackupAsync(
        string backupId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
            confirmationText,
            RestoreConfirmation,
            StringComparison.Ordinal))
        {
            throw new PalworldBackupValidationException(
                $"Confirmation text must be exactly '{RestoreConfirmation}'.");
        }

        var context = ResolveContext();
        var safeBackupId = ValidateBackupId(backupId);
        var containerName = ResolveContainerName();
        var playersOnline = await TryGetPlayersOnlineAsync(cancellationToken);

        await StopConfiguredContainerAsync(containerName, cancellationToken);

        PalworldBackupMetadata? preRestoreBackup = null;
        try
        {
            preRestoreBackup = await CreateArchiveAsync(
                context,
                PalworldBackupTypes.PreRestore,
                $"Automatic pre-restore backup before restoring {safeBackupId}.",
                "not-requested",
                cancellationToken);

            var selectedBackup = await GetRequiredBackupAsync(
                context.BackupPath,
                safeBackupId,
                cancellationToken);
            var selectedArchivePath = GetArchivePath(context.BackupPath, selectedBackup.Id);

            ValidateArchiveFile(selectedArchivePath);

            await ReplaceManagedPathFromArchiveAsync(
                context.ManagedPath,
                selectedArchivePath,
                cancellationToken);

            await StartConfiguredContainerAsync(containerName, cancellationToken);
            var healthCheckStatus = await CheckHealthAsync(containerName, cancellationToken);

            return new PalworldRestoreBackupResult(
                selectedBackup.Id,
                preRestoreBackup,
                playersOnline,
                "stopped",
                "started",
                healthCheckStatus,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Palworld backup restore failed after the configured container was stopped.");

            if (preRestoreBackup is not null)
            {
                await TryRestorePreRestoreBackupAsync(
                    context.ManagedPath,
                    GetArchivePath(context.BackupPath, preRestoreBackup.Id),
                    cancellationToken);
            }

            await TryStartContainerAfterRestoreFailureAsync(containerName, cancellationToken);

            if (exception is PalworldBackupException)
            {
                throw;
            }

            throw new PalworldBackupRestoreException(
                "Palworld backup restore failed. GamesHud attempted to preserve the previous state.",
                exception);
        }
    }

    public async Task DeleteBackupAsync(
        string backupId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
            confirmationText,
            DeleteConfirmation,
            StringComparison.Ordinal))
        {
            throw new PalworldBackupValidationException(
                $"Confirmation text must be exactly '{DeleteConfirmation}'.");
        }

        var context = ResolveContext();
        var safeBackupId = ValidateBackupId(backupId);
        var backup = await GetRequiredBackupAsync(context.BackupPath, safeBackupId, cancellationToken);
        var allBackups = await ReadAllMetadataAsync(context.BackupPath, cancellationToken);
        var completedBackups = allBackups.Where(IsCompleted).ToArray();

        if (completedBackups.Length <= 1 && IsCompleted(backup))
        {
            throw new PalworldBackupValidationException("The last valid backup cannot be deleted.");
        }

        var archivePath = GetArchivePath(context.BackupPath, safeBackupId);
        var metadataPath = GetMetadataPath(context.BackupPath, safeBackupId);

        File.Delete(archivePath);
        File.Delete(metadataPath);
    }

    public async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        var context = ResolveContext();
        var options = ResolveBackupOptions();
        var backups = await ReadAllMetadataAsync(context.BackupPath, cancellationToken);
        var completedBackups = backups
            .Where(IsCompleted)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();

        if (completedBackups.Length <= 1)
        {
            return;
        }

        var automaticBackups = completedBackups
            .Where(backup => backup.Type.Equals(PalworldBackupTypes.Automatic, StringComparison.Ordinal))
            .ToArray();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
        var automaticIdsToKeep = automaticBackups
            .Take(options.RetentionCount)
            .Select(backup => backup.Id)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = automaticBackups
            .Where(backup => !automaticIdsToKeep.Contains(backup.Id) || backup.CreatedAt < cutoff)
            .OrderBy(backup => backup.CreatedAt)
            .ToArray();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (completedBackups.Length <= 1)
            {
                return;
            }

            File.Delete(GetArchivePath(context.BackupPath, candidate.Id));
            File.Delete(GetMetadataPath(context.BackupPath, candidate.Id));
            completedBackups = completedBackups
                .Where(backup => !backup.Id.Equals(candidate.Id, StringComparison.Ordinal))
                .ToArray();
        }
    }

    private async Task<PalworldBackupMetadata> CreateArchiveAsync(
        ResolvedBackupContext context,
        string type,
        string? note,
        string? worldSaveStatus,
        CancellationToken cancellationToken)
    {
        var id = CreateBackupId();
        var filename = $"{id}{BackupExtension}";
        var archivePath = GetArchivePath(context.BackupPath, id);
        var temporaryArchivePath = Path.Combine(context.BackupPath, $".{id}{BackupExtension}.tmp");
        var metadataPath = GetMetadataPath(context.BackupPath, id);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using (var fileStream = File.Create(temporaryArchivePath))
            await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize))
            {
                TarFile.CreateFromDirectory(context.ManagedPath, gzipStream, includeBaseDirectory: false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryArchivePath, archivePath, overwrite: false);

            var metadata = new PalworldBackupMetadata(
                id,
                DateTimeOffset.UtcNow,
                (ulong)new FileInfo(archivePath).Length,
                filename,
                PalworldBackupStatuses.Completed,
                type,
                NormalizeNote(note),
                worldSaveStatus);
            var json = JsonSerializer.Serialize(metadata, JsonOptions);

            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            return metadata;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DeleteIfExists(temporaryArchivePath);
            DeleteIfExists(archivePath);
            DeleteIfExists(metadataPath);

            throw new PalworldBackupWriteException(
                "Palworld backup could not be created.",
                exception);
        }
    }

    private async Task<string> TryRequestWorldSaveAsync(
        bool requestWorldSave,
        CancellationToken cancellationToken)
    {
        if (!requestWorldSave)
        {
            return "not-requested";
        }

        try
        {
            await _palworldRestService.SaveWorldAsync(cancellationToken);
            var delaySeconds = ResolveBackupOptions().PreBackupSaveDelaySeconds;

            if (delaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }

            return "saved";
        }
        catch (PalworldRestConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld REST API is not configured. Creating backup without REST save.");

            return "unavailable";
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Palworld REST save failed. Creating backup without confirmed REST save.");

            return "failed";
        }
    }

    private async Task<int?> TryGetPlayersOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await _palworldRestService.GetMetricsAsync(cancellationToken);

            return metrics.CurrentPlayerNum;
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Unable to read Palworld players before restore.");

            return null;
        }
    }

    private async Task StopConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await _containerService.StopContainerAsync(
            containerName,
            ResolveBackupOptions().LifecycleTimeoutSeconds,
            cancellationToken);

        if (result is null)
        {
            throw new PalworldBackupLifecycleException("Configured Palworld container was not found.");
        }

        if (!result.Success)
        {
            throw new PalworldBackupLifecycleException("Configured Palworld container could not be stopped.");
        }
    }

    private async Task StartConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await _containerService.StartContainerAsync(containerName, cancellationToken);

        if (result is null)
        {
            throw new PalworldBackupLifecycleException("Configured Palworld container was not found.");
        }

        if (!result.Success)
        {
            throw new PalworldBackupLifecycleException("Configured Palworld container could not be started.");
        }
    }

    private async Task<string> CheckHealthAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var container = await _containerService.GetContainerDetailsAsync(containerName, cancellationToken);

        if (container is null)
        {
            return "container-not-found";
        }

        if (!container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            return "container-not-running";
        }

        try
        {
            await _palworldRestService.GetMetricsAsync(cancellationToken);

            return "healthy";
        }
        catch (PalworldRestException)
        {
            return "container-running-rest-unavailable";
        }
    }

    private async Task TryRestorePreRestoreBackupAsync(
        string managedPath,
        string preRestoreArchivePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReplaceManagedPathFromArchiveAsync(
                managedPath,
                preRestoreArchivePath,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unable to restore Palworld pre-restore backup after restore failure.");
        }
    }

    private async Task TryStartContainerAfterRestoreFailureAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartConfiguredContainerAsync(containerName, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unable to start configured Palworld container after restore failure.");
        }
    }

    private static async Task ReplaceManagedPathFromArchiveAsync(
        string managedPath,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var temporaryRestorePath = Path.Combine(
            Path.GetTempPath(),
            $"gameshud-palworld-restore-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryRestorePath);
            await ExtractArchiveAsync(archivePath, temporaryRestorePath, cancellationToken);

            DeleteDirectoryContents(managedPath);
            CopyDirectoryContents(temporaryRestorePath, managedPath);
        }
        finally
        {
            if (Directory.Exists(temporaryRestorePath))
            {
                Directory.Delete(temporaryRestorePath, recursive: true);
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new TarReader(gzipStream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = reader.GetNextEntry();

            if (entry is null)
            {
                break;
            }

            var destination = ResolveArchiveEntryPath(destinationPath, entry.Name);
            var entryType = entry.EntryType.ToString();

            if (entryType.Equals("Directory", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (!entryType.Equals("RegularFile", StringComparison.Ordinal)
                && !entryType.Equals("V7RegularFile", StringComparison.Ordinal))
            {
                throw new PalworldBackupRestoreException("Backup archive contains an unsupported entry type.");
            }

            var parentDirectory = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            if (entry.DataStream is null)
            {
                await File.WriteAllBytesAsync(destination, Array.Empty<byte>(), cancellationToken);
                continue;
            }

            await using var destinationStream = File.Create(destination);
            await entry.DataStream.CopyToAsync(destinationStream, cancellationToken);
        }
    }

    private static string ResolveArchiveEntryPath(string destinationPath, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)
            || Path.IsPathRooted(entryName)
            || entryName.Contains('\\', StringComparison.Ordinal))
        {
            throw new PalworldBackupRestoreException("Backup archive contains an invalid entry path.");
        }

        var segments = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment.Equals("..", StringComparison.Ordinal)))
        {
            throw new PalworldBackupRestoreException("Backup archive contains a path traversal entry.");
        }

        var candidatePath = Path.GetFullPath(Path.Combine(
            new[] { destinationPath }.Concat(segments).ToArray()));

        if (!IsInsidePath(destinationPath, candidatePath))
        {
            throw new PalworldBackupRestoreException("Backup archive entry resolves outside the restore directory.");
        }

        return candidatePath;
    }

    private static void CopyDirectoryContents(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.EnumerateDirectories(
            sourcePath,
            "*",
            SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(
            sourcePath,
            "*",
            SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void DeleteDirectoryContents(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path))
        {
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<PalworldBackupMetadata> GetRequiredBackupAsync(
        string backupPath,
        string backupId,
        CancellationToken cancellationToken)
    {
        var safeBackupId = ValidateBackupId(backupId);
        var metadataPath = GetMetadataPath(backupPath, safeBackupId);

        if (!File.Exists(metadataPath))
        {
            throw new PalworldBackupNotFoundException("Backup was not found.");
        }

        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);

        if (!IsCompleted(metadata))
        {
            throw new PalworldBackupValidationException("Only completed backups can be used.");
        }

        ValidateArchiveFile(GetArchivePath(backupPath, metadata.Id));

        return metadata;
    }

    private async Task<IReadOnlyCollection<PalworldBackupMetadata>> ReadAllMetadataAsync(
        string backupPath,
        CancellationToken cancellationToken)
    {
        var backups = new List<PalworldBackupMetadata>();

        foreach (var metadataPath in Directory.EnumerateFiles(backupPath, $"*{MetadataExtension}"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);

                if (metadata.Filename.EndsWith(BackupExtension, StringComparison.Ordinal)
                    && metadata.Id.Equals(
                        Path.GetFileNameWithoutExtension(
                            Path.GetFileNameWithoutExtension(metadata.Filename)),
                        StringComparison.Ordinal)
                    && File.Exists(GetArchivePath(backupPath, metadata.Id)))
                {
                    backups.Add(metadata with
                    {
                        SizeBytes = (ulong)new FileInfo(GetArchivePath(backupPath, metadata.Id)).Length
                    });
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Ignoring malformed Palworld backup metadata.");
            }
        }

        return backups
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    }

    private static async Task<PalworldBackupMetadata> ReadMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metadataPath);
        var metadata = await JsonSerializer.DeserializeAsync<PalworldBackupMetadata>(
            stream,
            JsonOptions,
            cancellationToken);

        return metadata
            ?? throw new PalworldBackupValidationException("Backup metadata is empty.");
    }

    private ResolvedBackupContext ResolveContext()
    {
        var managedPath = ResolveExistingDirectory(_options.Value.ManagedPath, "Palworld managed path is not configured.");
        var backupPath = ResolveBackupPath(_options.Value.BackupPath);

        if (PathsOverlap(managedPath, backupPath))
        {
            throw new PalworldBackupConfigurationException(
                "Palworld backup path must be separate from the managed path.");
        }

        return new ResolvedBackupContext(managedPath, backupPath);
    }

    private static string ResolveExistingDirectory(string path, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PalworldBackupConfigurationException(emptyMessage);
        }

        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new PalworldBackupConfigurationException("Configured Palworld managed path was not found.");
        }

        return fullPath;
    }

    private static string ResolveBackupPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PalworldBackupConfigurationException("Palworld backup path is not configured.");
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        return fullPath;
    }

    private string ResolveContainerName()
    {
        var containerName = _options.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new PalworldBackupConfigurationException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }

    private PalworldBackupOptions ResolveBackupOptions()
    {
        var options = _options.Value.Backups;

        return new PalworldBackupOptions
        {
            AutomaticEnabled = options.AutomaticEnabled,
            AutomaticIntervalMinutes = Math.Clamp(options.AutomaticIntervalMinutes, 5, 10_080),
            RetentionCount = Math.Clamp(options.RetentionCount, 1, 10_000),
            RetentionDays = Math.Clamp(options.RetentionDays, 1, 365),
            PreBackupSaveDelaySeconds = Math.Clamp(options.PreBackupSaveDelaySeconds, 0, 30),
            LifecycleTimeoutSeconds = Math.Clamp(options.LifecycleTimeoutSeconds, 1, 120)
        };
    }

    private PalworldBackupSchedule CreateSchedule()
    {
        var options = ResolveBackupOptions();

        return new PalworldBackupSchedule(
            options.AutomaticEnabled,
            options.AutomaticIntervalMinutes,
            options.RetentionCount,
            options.RetentionDays,
            _scheduleState.NextScheduledAt);
    }

    private static void ValidateCreateOptions(PalworldBackupCreateOptions options)
    {
        if (options.Type is not PalworldBackupTypes.Manual
            and not PalworldBackupTypes.Automatic
            and not PalworldBackupTypes.PreRestore
            and not PalworldBackupTypes.PreUpdate)
        {
            throw new PalworldBackupValidationException("Backup type is invalid.");
        }

        _ = NormalizeNote(options.Note);
    }

    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var normalizedNote = note.Trim();

        if (normalizedNote.Length > MaxNoteLength
            || normalizedNote.Contains('\r', StringComparison.Ordinal)
            || normalizedNote.Contains('\n', StringComparison.Ordinal))
        {
            throw new PalworldBackupValidationException(
                $"Backup note must be {MaxNoteLength} characters or fewer and cannot contain line breaks.");
        }

        return normalizedNote;
    }

    private static string ValidateBackupId(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId)
            || !Regex.IsMatch(backupId, @"\Apalworld-\d{17}-[a-f0-9]{12}\z", RegexOptions.CultureInvariant))
        {
            throw new PalworldBackupValidationException("Backup id is invalid.");
        }

        return backupId;
    }

    private static void ValidateArchiveFile(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new PalworldBackupNotFoundException("Backup archive was not found.");
        }

        if (!archivePath.EndsWith(BackupExtension, StringComparison.Ordinal))
        {
            throw new PalworldBackupValidationException("Backup archive format is invalid.");
        }
    }

    private static string CreateBackupId()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

        return string.Create(
            CultureInfo.InvariantCulture,
            $"palworld-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{suffix}");
    }

    private static string GetArchivePath(string backupPath, string backupId)
    {
        return Path.Combine(backupPath, $"{ValidateBackupId(backupId)}{BackupExtension}");
    }

    private static string GetMetadataPath(string backupPath, string backupId)
    {
        return Path.Combine(backupPath, $"{ValidateBackupId(backupId)}{MetadataExtension}");
    }

    private static bool IsCompleted(PalworldBackupMetadata backup)
    {
        return backup.Status.Equals(PalworldBackupStatuses.Completed, StringComparison.Ordinal);
    }

    private static bool PathsOverlap(string firstPath, string secondPath)
    {
        return IsInsidePath(firstPath, secondPath)
            || IsInsidePath(secondPath, firstPath);
    }

    private static bool IsInsidePath(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ResolvedBackupContext(
        string ManagedPath,
        string BackupPath);
}
