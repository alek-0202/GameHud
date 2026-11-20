using System.Formats.Tar;
using System.IO.Compression;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldBackupTests
{
    [Fact]
    public async Task CreateBackupRequestsWorldSaveAndStoresMetadata()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var restService = new RecordingPalworldRestService();
        var service = CreateService(directories, restService);

        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                "Before update",
                RequestWorldSave: true),
            CancellationToken.None);

        Assert.Equal(PalworldBackupTypes.Manual, backup.Type);
        Assert.Equal(PalworldBackupStatuses.Completed, backup.Status);
        Assert.Equal("Before update", backup.Note);
        Assert.Equal("saved", backup.WorldSaveStatus);
        Assert.True(backup.SizeBytes > 0);
        Assert.Equal(1, restService.SaveWorldCalls);
        Assert.True(File.Exists(Path.Combine(directories.BackupPath, backup.Filename)));
        Assert.True(File.Exists(Path.Combine(directories.BackupPath, $"{backup.Id}.json")));
    }

    [Fact]
    public async Task CreateBackupContinuesWhenRestSaveIsUnavailable()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var restService = new RecordingPalworldRestService
        {
            SaveWorldException = new PalworldRestUnavailableException("REST unavailable.")
        };
        var service = CreateService(directories, restService);

        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: true),
            CancellationToken.None);

        Assert.Equal("failed", backup.WorldSaveStatus);
        Assert.True(File.Exists(Path.Combine(directories.BackupPath, backup.Filename)));
    }

    [Fact]
    public async Task RetentionKeepsManualBackupsAndPrunesAutomaticBackups()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var service = CreateService(
            directories,
            backupOptions: new PalworldBackupOptions
            {
                RetentionCount = 1,
                RetentionDays = 7,
                PreBackupSaveDelaySeconds = 0
            });

        var manualBackup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Automatic,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        await Task.Delay(2);
        var newestAutomatic = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Automatic,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        var summary = await service.GetBackupsAsync(CancellationToken.None);

        Assert.Contains(summary.Backups, backup => backup.Id == manualBackup.Id);
        Assert.Contains(summary.Backups, backup => backup.Id == newestAutomatic.Id);
        Assert.Single(summary.Backups, backup => backup.Type == PalworldBackupTypes.Automatic);
    }

    [Fact]
    public async Task DeleteBackupRequiresConfirmationAndNeverDeletesLastValidBackup()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var service = CreateService(directories);
        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);

        await Assert.ThrowsAsync<PalworldBackupValidationException>(
            () => service.DeleteBackupAsync(
                backup.Id,
                "DELETE",
                CancellationToken.None));
        await Assert.ThrowsAsync<PalworldBackupValidationException>(
            () => service.DeleteBackupAsync(
                backup.Id,
                "DELETE PALWORLD BACKUP",
                CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(directories.BackupPath, backup.Filename)));
    }

    [Fact]
    public async Task DeleteBackupRemovesArchiveAndMetadataWhenAnotherBackupExists()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var service = CreateService(directories);
        var firstBackup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        await Task.Delay(2);
        await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);

        await service.DeleteBackupAsync(
            firstBackup.Id,
            "DELETE PALWORLD BACKUP",
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(directories.BackupPath, firstBackup.Filename)));
        Assert.False(File.Exists(Path.Combine(directories.BackupPath, $"{firstBackup.Id}.json")));
    }

    [Fact]
    public async Task RestoreRequiresStrongConfirmationBeforeLifecycle()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var containerService = new RecordingContainerService();
        var service = CreateService(directories, containerService: containerService);
        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);

        await Assert.ThrowsAsync<PalworldBackupValidationException>(
            () => service.RestoreBackupAsync(
                backup.Id,
                "RESTORE",
                CancellationToken.None));

        Assert.Empty(containerService.StopRequests);
        Assert.Empty(containerService.StartRequests);
    }

    [Fact]
    public async Task RestoreCreatesPreRestoreBackupAndTouchesOnlyConfiguredPalworldContainer()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var containerService = new RecordingContainerService();
        var service = CreateService(directories, containerService: containerService);
        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(directories.ManagedPath, "Level.sav"),
            "changed");

        var result = await service.RestoreBackupAsync(
            backup.Id,
            "RESTORE PALWORLD BACKUP",
            CancellationToken.None);
        var restoredText = await File.ReadAllTextAsync(Path.Combine(directories.ManagedPath, "Level.sav"));
        var summary = await service.GetBackupsAsync(CancellationToken.None);

        Assert.Equal("world-data", restoredText);
        Assert.Equal(3, result.PlayersOnlineBeforeRestore);
        Assert.Equal(new[] { "palworld-server" }, containerService.StopRequests);
        Assert.Equal(new[] { "palworld-server" }, containerService.StartRequests);
        Assert.Empty(containerService.RestartRequests);
        Assert.Contains(summary.Backups, item => item.Type == PalworldBackupTypes.PreRestore);
    }

    [Fact]
    public async Task RestoreRejectsPathTraversalBackupId()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var containerService = new RecordingContainerService();
        var service = CreateService(directories, containerService: containerService);

        await Assert.ThrowsAsync<PalworldBackupValidationException>(
            () => service.RestoreBackupAsync(
                "../outside",
                "RESTORE PALWORLD BACKUP",
                CancellationToken.None));

        Assert.Empty(containerService.StopRequests);
    }

    [Fact]
    public async Task RestoreAttemptsPreRestoreRollbackWhenArchiveIsInvalid()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var containerService = new RecordingContainerService();
        var service = CreateService(directories, containerService: containerService);
        var backup = await service.CreateBackupAsync(
            new PalworldBackupCreateOptions(
                PalworldBackupTypes.Manual,
                null,
                RequestWorldSave: false),
            CancellationToken.None);
        var originalText = await File.ReadAllTextAsync(Path.Combine(directories.ManagedPath, "Level.sav"));
        await File.WriteAllTextAsync(
            Path.Combine(directories.BackupPath, backup.Filename),
            "not a gzip archive");

        await Assert.ThrowsAsync<PalworldBackupRestoreException>(
            () => service.RestoreBackupAsync(
                backup.Id,
                "RESTORE PALWORLD BACKUP",
                CancellationToken.None));

        Assert.Equal(
            originalText,
            await File.ReadAllTextAsync(Path.Combine(directories.ManagedPath, "Level.sav")));
        Assert.Equal(new[] { "palworld-server" }, containerService.StopRequests);
        Assert.Equal(new[] { "palworld-server" }, containerService.StartRequests);
        Assert.Empty(containerService.RestartRequests);
    }

    [Fact]
    public async Task BackupPathCannotOverlapManagedPath()
    {
        using var directories = TemporaryBackupDirectories.CreateBackupInsideManagedPath();
        var service = CreateService(directories);

        await Assert.ThrowsAsync<PalworldBackupConfigurationException>(
            () => service.GetBackupsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateBackupPropagatesCancellation()
    {
        using var directories = TemporaryBackupDirectories.Create();
        var service = CreateService(directories);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CreateBackupAsync(
                new PalworldBackupCreateOptions(
                    PalworldBackupTypes.Manual,
                    null,
                    RequestWorldSave: false),
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task RestServicePostsSaveEndpoint()
    {
        var handler = new RecordingHttpHandler();
        var service = new PalworldRestService(
            new HttpClient(handler),
            Options.Create(new PalworldOptions
            {
                RestApi = new PalworldRestApiOptions
                {
                    BaseUrl = "http://palworld.local:8212",
                    Username = "admin",
                    Password = "admin-password"
                }
            }));

        await service.SaveWorldAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("http://palworld.local:8212/v1/api/save", handler.LastUri?.ToString());
    }

    private static PalworldBackupService CreateService(
        TemporaryBackupDirectories directories,
        IPalworldRestService? restService = null,
        IContainerService? containerService = null,
        PalworldBackupOptions? backupOptions = null)
    {
        return new PalworldBackupService(
            Options.Create(new PalworldOptions
            {
                ManagedPath = directories.ManagedPath,
                BackupPath = directories.BackupPath,
                ContainerName = "palworld-server",
                Backups = backupOptions ?? new PalworldBackupOptions
                {
                    RetentionCount = 24,
                    RetentionDays = 7,
                    PreBackupSaveDelaySeconds = 0,
                    LifecycleTimeoutSeconds = 10
                }
            }),
            restService ?? new RecordingPalworldRestService(),
            containerService ?? new RecordingContainerService(),
            new RecordingNotificationService(),
            new PalworldBackupScheduleState(),
            NullLogger<PalworldBackupService>.Instance);
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public NotificationSettingsResponse GetSettings()
        {
            return new NotificationSettingsResponse(false, true, true, true, false, 60, null, null);
        }

        public Task<NotificationSendResult> SendTestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new NotificationSendResult(true, "sent", DateTimeOffset.UtcNow));
        }

        public Task<NotificationSendResult> NotifyAsync(
            NotificationEvent notificationEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new NotificationSendResult(true, "sent", DateTimeOffset.UtcNow));
        }
    }

    private sealed class TemporaryBackupDirectories : IDisposable
    {
        private TemporaryBackupDirectories(string rootPath, string managedPath, string backupPath)
        {
            RootPath = rootPath;
            ManagedPath = managedPath;
            BackupPath = backupPath;
        }

        public string RootPath { get; }

        public string ManagedPath { get; }

        public string BackupPath { get; }

        public static TemporaryBackupDirectories Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"gameshud-palworld-backups-{Guid.NewGuid():N}");
            var managedPath = Path.Combine(root, "managed");
            var backupPath = Path.Combine(root, "backups");
            var nestedPath = Path.Combine(managedPath, "Pal", "Saved");

            Directory.CreateDirectory(nestedPath);
            Directory.CreateDirectory(backupPath);
            File.WriteAllText(Path.Combine(managedPath, "Level.sav"), "world-data");
            File.WriteAllText(Path.Combine(nestedPath, "Players.sav"), "players-data");

            return new TemporaryBackupDirectories(root, managedPath, backupPath);
        }

        public static TemporaryBackupDirectories CreateBackupInsideManagedPath()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"gameshud-palworld-backups-{Guid.NewGuid():N}");
            var managedPath = Path.Combine(root, "managed");
            var backupPath = Path.Combine(managedPath, "backups");

            Directory.CreateDirectory(managedPath);

            return new TemporaryBackupDirectories(root, managedPath, backupPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class RecordingPalworldRestService : IPalworldRestService
    {
        public Exception? SaveWorldException { get; set; }

        public int SaveWorldCalls { get; private set; }

        public Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldRestInfo("v1.0.3", "Palworld", null, "world-guid"));
        }

        public Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldRestPlayers([]));
        }

        public Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldRestSettings(10, "Palworld", null));
        }

        public Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldRestMetrics(60, 3, 16.1, 10, 300, 2, 4));
        }

        public Task SaveWorldAsync(CancellationToken cancellationToken)
        {
            SaveWorldCalls++;

            if (SaveWorldException is not null)
            {
                throw SaveWorldException;
            }

            return Task.CompletedTask;
        }

        public Task AnnounceAsync(
            string message,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContainerService : IContainerService
    {
        public List<string> StartRequests { get; } = new();

        public List<string> StopRequests { get; } = new();

        public List<string> RestartRequests { get; } = new();

        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<ContainerResponse>>(Array.Empty<ContainerResponse>());
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerDetailsResponse?>(new ContainerDetailsResponse(
                containerId,
                containerId,
                "palworld",
                "image",
                "running",
                "running",
                "2026-01-01T00:00:00Z",
                "2026-01-01T00:00:00Z",
                "",
                0,
                "linux",
                "overlay2",
                [],
                [],
                [],
                new Dictionary<string, string>()));
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            string stream,
            string? search,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLogsResponse?>(null);
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            StartRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(containerId, "start"));
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            StopRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(containerId, "stop"));
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            RestartRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(containerId, "restart"));
        }

        private static ContainerLifecycleActionResponse CreateLifecycleResponse(
            string containerId,
            string action)
        {
            return new ContainerLifecycleActionResponse(
                containerId,
                action,
                true,
                "done",
                "running",
                "running",
                "2026-01-01T00:00:00Z");
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
