using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using GamesHud.Api.Palworld.Updates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldUpdateTests
{
    [Fact]
    public async Task CheckForUpdatesComparesInstalledAndRemoteSteamBuilds()
    {
        var service = CreateService();

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("v1.0.3", status.InstalledVersion);
        Assert.Equal("Steam build 200", status.AvailableVersion);
        Assert.Equal(PalworldUpdateStatuses.UpdateAvailable, status.UpdateStatus);
    }

    [Fact]
    public void ExtractSteamBuildIdPrefersPublicBranch()
    {
        var output = """
            "branches"
            {
              "beta"
              {
                "buildid" "999"
              }
              "public"
              {
                "buildid" "123"
              }
            }
            """;

        var result = PalworldUpdateService.ExtractSteamBuildId(output);

        Assert.Equal("123", result);
    }

    [Fact]
    public async Task ApplyUpdateRequiresStrongConfirmation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PalworldUpdateValidationException>(
            () => service.ApplyUpdateAsync("UPDATE", CancellationToken.None));
    }

    [Fact]
    public async Task ApplyUpdateRunsRequiredStepsInOrderAndTouchesOnlyConfiguredContainer()
    {
        var events = new List<string>();
        var restService = new RecordingRestService(events);
        var backupService = new RecordingBackupService(events);
        var containerService = new RecordingContainerService(events);
        var runner = new RecordingUpdateRunner(events);
        var service = CreateService(
            restService: restService,
            backupService: backupService,
            containerService: containerService,
            updateRunner: runner);

        var result = await service.ApplyUpdateAsync(
            PalworldUpdateService.UpdateConfirmation,
            CancellationToken.None);
        var requiredEvents = events
            .Where(item => item is "save" or "backup" or "stop" or "update" or "start" or "health")
            .ToArray();

        Assert.Equal(
            ["save", "backup", "stop", "update", "start", "health"],
            requiredEvents);
        Assert.Equal("palworld-server", containerService.StoppedContainers.Single());
        Assert.Equal("palworld-server", containerService.StartedContainers.Single());
        Assert.Empty(containerService.RestartedContainers);
        Assert.True(result.UpdateApplied);
        Assert.Equal("pre-update-backup", result.BackupId);
    }

    [Theory]
    [InlineData(PalworldUpdateSteps.Save)]
    [InlineData(PalworldUpdateSteps.Backup)]
    [InlineData(PalworldUpdateSteps.Stop)]
    [InlineData(PalworldUpdateSteps.Update)]
    [InlineData(PalworldUpdateSteps.Start)]
    [InlineData(PalworldUpdateSteps.Health)]
    public async Task ApplyUpdateReportsStepFailures(string failedStep)
    {
        var events = new List<string>();
        var restService = new RecordingRestService(events)
        {
            FailSave = failedStep == PalworldUpdateSteps.Save,
            FailHealth = failedStep == PalworldUpdateSteps.Health
        };
        var backupService = new RecordingBackupService(events)
        {
            FailCreate = failedStep == PalworldUpdateSteps.Backup
        };
        var containerService = new RecordingContainerService(events)
        {
            FailStop = failedStep == PalworldUpdateSteps.Stop,
            FailStart = failedStep == PalworldUpdateSteps.Start
        };
        var runner = new RecordingUpdateRunner(events)
        {
            FailUpdate = failedStep == PalworldUpdateSteps.Update
        };
        var service = CreateService(
            restService: restService,
            backupService: backupService,
            containerService: containerService,
            updateRunner: runner);

        var exception = await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.Equal(failedStep, exception.FailedStep);
    }

    [Fact]
    public async Task UpdateFailureAfterStopAttemptsToStartConfiguredContainer()
    {
        var events = new List<string>();
        var containerService = new RecordingContainerService(events);
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: containerService,
            updateRunner: new RecordingUpdateRunner(events)
            {
                FailUpdate = true
            });

        await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.Equal(["palworld-server"], containerService.StoppedContainers);
        Assert.Equal(["palworld-server"], containerService.StartedContainers);
        Assert.Empty(containerService.RestartedContainers);
    }

    [Fact]
    public async Task ApplyUpdateRequiresUpdateOnBoot()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            UpdateOnBootEnabled = false
        });

        await Assert.ThrowsAsync<PalworldUpdateValidationException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));
    }

    private static PalworldUpdateService CreateService(
        IPalworldRestService? restService = null,
        IPalworldBackupService? backupService = null,
        IContainerService? containerService = null,
        IPalworldContainerCommandService? commandService = null,
        IPalworldUpdateRunner? updateRunner = null)
    {
        return new PalworldUpdateService(
            Options.Create(new PalworldOptions
            {
                ContainerName = "palworld-server"
            }),
            restService ?? new RecordingRestService(new List<string>()),
            backupService ?? new RecordingBackupService(new List<string>()),
            containerService ?? new RecordingContainerService(new List<string>()),
            commandService ?? new RecordingCommandService(),
            updateRunner ?? new RecordingUpdateRunner(new List<string>()),
            new RecordingNotificationService(),
            NullLogger<PalworldUpdateService>.Instance);
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

    private sealed class RecordingRestService : IPalworldRestService
    {
        private readonly List<string> _events;
        private int _metricsCalls;

        public RecordingRestService(List<string> events)
        {
            _events = events;
        }

        public bool FailSave { get; set; }

        public bool FailHealth { get; set; }

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
            _metricsCalls++;

            if (_metricsCalls == 1)
            {
                _events.Add("players");
            }
            else
            {
                _events.Add("health");
            }

            if (FailHealth && _metricsCalls > 1)
            {
                throw new PalworldRestUnavailableException("health failed");
            }

            return Task.FromResult(new PalworldRestMetrics(60, 3, 16.1, 10, 300, 2, 4));
        }

        public Task SaveWorldAsync(CancellationToken cancellationToken)
        {
            _events.Add("save");

            if (FailSave)
            {
                throw new PalworldRestUnavailableException("save failed");
            }

            return Task.CompletedTask;
        }

        public Task AnnounceAsync(
            string message,
            CancellationToken cancellationToken)
        {
            _events.Add("announce");

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBackupService : IPalworldBackupService
    {
        private readonly List<string> _events;

        public RecordingBackupService(List<string> events)
        {
            _events = events;
        }

        public bool FailCreate { get; set; }

        public Task<PalworldBackupSummary> GetBackupsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldBackupSummary(
                new PalworldBackupSchedule(false, 360, 24, 7, null),
                new PalworldBackupStorage(0, 0),
                null,
                []));
        }

        public Task<PalworldBackupMetadata?> GetBackupAsync(
            string backupId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PalworldBackupMetadata?>(null);
        }

        public Task<string> GetBackupFilePathAsync(
            string backupId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<PalworldBackupMetadata> CreateBackupAsync(
            PalworldBackupCreateOptions options,
            CancellationToken cancellationToken)
        {
            _events.Add("backup");

            if (FailCreate)
            {
                throw new PalworldBackupWriteException(
                    "backup failed",
                    new IOException("backup failed"));
            }

            Assert.Equal(PalworldBackupTypes.PreUpdate, options.Type);
            Assert.False(options.RequestWorldSave);

            return Task.FromResult(new PalworldBackupMetadata(
                "pre-update-backup",
                DateTimeOffset.UtcNow,
                100,
                "backup.tar.gz",
                PalworldBackupStatuses.Completed,
                PalworldBackupTypes.PreUpdate,
                "Automatic pre-update backup.",
                "not-requested"));
        }

        public Task<PalworldRestoreBackupResult> RestoreBackupAsync(
            string backupId,
            string confirmationText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteBackupAsync(
            string backupId,
            string confirmationText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ApplyRetentionAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContainerService : IContainerService
    {
        private readonly List<string> _events;

        public RecordingContainerService(List<string> events)
        {
            _events = events;
        }

        public bool FailStop { get; set; }

        public bool FailStart { get; set; }

        public List<string> StartedContainers { get; } = new();

        public List<string> StoppedContainers { get; } = new();

        public List<string> RestartedContainers { get; } = new();

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
            _events.Add("start");
            StartedContainers.Add(containerId);

            if (FailStart)
            {
                return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                    containerId,
                    "start",
                    false,
                    "exited"));
            }

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "start",
                true,
                "running"));
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            _events.Add("stop");
            StoppedContainers.Add(containerId);

            if (FailStop)
            {
                return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                    containerId,
                    "stop",
                    false,
                    "running"));
            }

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "stop",
                true,
                "exited"));
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            RestartedContainers.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "restart",
                true,
                "running"));
        }

        private static ContainerLifecycleActionResponse CreateLifecycleResponse(
            string containerId,
            string action,
            bool success,
            string currentState)
        {
            return new ContainerLifecycleActionResponse(
                containerId,
                action,
                success,
                "done",
                "running",
                currentState,
                "2026-01-01T00:00:00Z");
        }
    }

    private sealed class RecordingCommandService : IPalworldContainerCommandService
    {
        public bool UpdateOnBootEnabled { get; set; } = true;

        public Task<PalworldContainerCommandResult> ExecuteAsync(
            string containerName,
            IReadOnlyList<string> command,
            CancellationToken cancellationToken)
        {
            var joinedCommand = string.Join(" ", command);

            if (joinedCommand.Contains("UPDATE_ON_BOOT", StringComparison.Ordinal))
            {
                return Task.FromResult(new PalworldContainerCommandResult(
                    0,
                    UpdateOnBootEnabled ? "true" : "false"));
            }

            if (joinedCommand.Contains("app_info_print", StringComparison.Ordinal))
            {
                return Task.FromResult(new PalworldContainerCommandResult(
                    0,
                    """
                    "branches"
                    {
                      "public"
                      {
                        "buildid" "200"
                      }
                    }
                    """));
            }

            return Task.FromResult(new PalworldContainerCommandResult(
                0,
                "\"buildid\" \"100\""));
        }
    }

    private sealed class RecordingUpdateRunner : IPalworldUpdateRunner
    {
        private readonly List<string> _events;

        public RecordingUpdateRunner(List<string> events)
        {
            _events = events;
        }

        public bool FailUpdate { get; set; }

        public Task<string> PrepareUpdateAsync(
            string containerName,
            CancellationToken cancellationToken)
        {
            _events.Add("update");

            if (FailUpdate)
            {
                throw new PalworldUpdateCommandException("update failed");
            }

            return Task.FromResult("update-on-boot");
        }
    }
}
