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
    public async Task CheckForUpdatesComparesInstalledAndRemoteSteamManifests()
    {
        var service = CreateService();

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("v1.0.3", status.InstalledVersion);
        Assert.Equal("100", status.InstalledBuild);
        Assert.Equal("Steam manifest 200", status.AvailableVersion);
        Assert.Equal("200", status.AvailableBuild);
        Assert.Equal(PalworldUpdateStatuses.UpdateAvailable, status.UpdateStatus);
        Assert.True(status.UpdateReady);
        Assert.Equal(PalworldUpdateReadinessStatuses.Ready, status.UpdateReadinessStatus);
    }

    [Fact]
    public async Task CheckForUpdatesReportsUpToDateWhenManifestsMatch()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            LocalManifest = "200"
        });

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(PalworldUpdateStatuses.UpToDate, status.UpdateStatus);
        Assert.Equal("Steam manifest 200", status.AvailableVersion);
    }

    [Fact]
    public async Task CheckForUpdatesReportsUnavailableWhenRemoteManifestCannotBeRead()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            RemoteAppInfoOutput = "malformed"
        });

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(PalworldUpdateStatuses.Unavailable, status.UpdateStatus);
        Assert.Null(status.AvailableVersion);
        Assert.Null(status.AvailableBuild);
    }

    [Fact]
    public async Task CheckForUpdatesReportsUnavailableWhenInstalledManifestCannotBeRead()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            LocalManifest = string.Empty
        });

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(PalworldUpdateStatuses.Unavailable, status.UpdateStatus);
        Assert.Equal("Steam manifest 200", status.AvailableVersion);
        Assert.Null(status.InstalledBuild);
    }

    [Fact]
    public async Task CheckForUpdatesDoesNotMutateServerOrSendNotification()
    {
        var events = new List<string>();
        var notificationService = new RecordingNotificationService();
        var containerService = new RecordingContainerService(events);
        var service = CreateService(
            restService: new RecordingRestService(events),
            containerService: containerService,
            notificationService: notificationService);

        await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Empty(events);
        Assert.Empty(containerService.StartedContainers);
        Assert.Empty(containerService.StoppedContainers);
        Assert.Empty(containerService.RestartedContainers);
        Assert.Equal(0, notificationService.NotificationCount);
    }

    [Fact]
    public async Task CheckForUpdatesStillReportsAvailableWhenUpdateOnBootIsFalse()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            UpdateOnBootValue = "false"
        });

        var status = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(PalworldUpdateStatuses.UpdateAvailable, status.UpdateStatus);
        Assert.False(status.UpdateReady);
        Assert.Equal(PalworldUpdateReadinessStatuses.NotConfigured, status.UpdateReadinessStatus);
    }

    [Fact]
    public void ExtractLatestPublicDepotManifestIdReadsRealisticSteamAppInfo()
    {
        var output = """
            "2394010"
            {
              "depots"
              {
                "2394012"
                {
                  "manifests"
                  {
                    "public"
                    {
                      "gid" "1234567890123456789"
                    }
                    "beta"
                    {
                      "gid" "999"
                    }
                  }
                }
              }
            }
            """;

        var result = PalworldUpdateService.ExtractLatestPublicDepotManifestId(output);

        Assert.Equal("1234567890123456789", result);
    }

    [Fact]
    public void ExtractLatestPublicDepotManifestIdReturnsNullForMalformedOutput()
    {
        var result = PalworldUpdateService.ExtractLatestPublicDepotManifestId("not vdf");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractInstalledDepotManifestIdReadsAppManifestDepot()
    {
        var output = """
            "AppState"
            {
              "InstalledDepots"
              {
                "1006"
                {
                  "manifest" "111"
                }
                "2394012"
                {
                  "manifest" "222"
                }
              }
            }
            """;

        var result = PalworldUpdateService.ExtractInstalledDepotManifestId(output);

        Assert.Equal("222", result);
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
        var events = new List<string>();
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService
            {
                UpdateOnBootValue = "false"
            },
            updateRunner: new RecordingUpdateRunner(events));

        await Assert.ThrowsAsync<PalworldUpdateNotConfiguredException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.DoesNotContain("stop", events);
        Assert.DoesNotContain("update", events);
        Assert.DoesNotContain("start", events);
    }

    [Fact]
    public async Task ApplyUpdateBlocksWhenUpdateOnBootIsAbsentBeforeStop()
    {
        var events = new List<string>();
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService
            {
                UpdateOnBootValue = null
            },
            updateRunner: new RecordingUpdateRunner(events));

        await Assert.ThrowsAsync<PalworldUpdateNotConfiguredException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.DoesNotContain("stop", events);
        Assert.DoesNotContain("update", events);
        Assert.DoesNotContain("start", events);
    }

    [Fact]
    public async Task ApplyUpdateBlocksWhenUpdateOnBootCannotBeInspected()
    {
        var events = new List<string>();
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService
            {
                FailEnvironmentInspection = true
            },
            updateRunner: new RecordingUpdateRunner(events));

        await Assert.ThrowsAsync<PalworldUpdateNotConfiguredException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.DoesNotContain("stop", events);
        Assert.DoesNotContain("update", events);
        Assert.DoesNotContain("start", events);
    }

    [Fact]
    public async Task ContainerRunningWithOldManifestAfterStartIsNotUpdateSuccess()
    {
        var events = new List<string>();
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService
            {
                LocalManifestAfterUpdate = "100"
            },
            updateRunner: new RecordingUpdateRunner(events));

        var exception = await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.Equal(PalworldUpdateSteps.VersionCheck, exception.FailedStep);
        Assert.Contains("start", events);
        Assert.Contains("health", events);
    }

    [Fact]
    public async Task ExpectedManifestInstalledAfterStartIsUpdateSuccess()
    {
        var service = CreateService(commandService: new RecordingCommandService
        {
            LocalManifestAfterUpdate = "200"
        });

        var result = await service.ApplyUpdateAsync(
            PalworldUpdateService.UpdateConfirmation,
            CancellationToken.None);

        Assert.True(result.UpdateApplied);
        Assert.Equal(PalworldUpdateStatuses.Applied, result.UpdateStatus);
    }

    [Fact]
    public async Task ApplyUpdateWaitsForPalworldRestAfterStart()
    {
        var events = new List<string>();
        var restService = new RecordingRestService(events)
        {
            HealthFailuresBeforeSuccess = 2
        };
        var service = CreateService(
            restService: restService,
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService(),
            updateRunner: new RecordingUpdateRunner(events));

        var result = await service.ApplyUpdateAsync(
            PalworldUpdateService.UpdateConfirmation,
            CancellationToken.None);

        Assert.True(result.UpdateApplied);
        Assert.Equal(3, events.Count(static item => item == "health"));
    }

    [Fact]
    public async Task ApplyUpdateReportsHealthTimeoutWhenPalworldRestNeverReturns()
    {
        var events = new List<string>();
        var restService = new RecordingRestService(events)
        {
            FailHealth = true
        };
        var service = CreateService(
            restService: restService,
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events),
            commandService: new RecordingCommandService(),
            updateRunner: new RecordingUpdateRunner(events));

        var exception = await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.Equal(PalworldUpdateSteps.Health, exception.FailedStep);
        Assert.Contains("did not become healthy", exception.Message);
    }

    [Fact]
    public async Task DuplicateUpdateIsBlocked()
    {
        var operationState = new PalworldUpdateOperationState();
        using var _ = operationState.TryBegin();
        var service = CreateService(operationState: operationState);

        await Assert.ThrowsAsync<PalworldUpdateConflictException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));
    }

    [Fact]
    public async Task StopFailurePreventsUpdate()
    {
        var events = new List<string>();
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events)
            {
                FailStop = true
            },
            updateRunner: new RecordingUpdateRunner(events));

        await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.DoesNotContain("update", events);
        Assert.DoesNotContain("start", events);
    }

    [Fact]
    public async Task StartFailureDoesNotRepeatUpdate()
    {
        var events = new List<string>();
        var runner = new RecordingUpdateRunner(events);
        var service = CreateService(
            restService: new RecordingRestService(events),
            backupService: new RecordingBackupService(events),
            containerService: new RecordingContainerService(events)
            {
                FailStart = true
            },
            updateRunner: runner);

        await Assert.ThrowsAsync<PalworldUpdateFailedException>(
            () => service.ApplyUpdateAsync(
                PalworldUpdateService.UpdateConfirmation,
                CancellationToken.None));

        Assert.Equal(1, runner.UpdateCount);
    }

    private static PalworldUpdateService CreateService(
        IPalworldRestService? restService = null,
        IPalworldBackupService? backupService = null,
        IContainerService? containerService = null,
        IPalworldContainerCommandService? commandService = null,
        IPalworldUpdateRunner? updateRunner = null,
        INotificationService? notificationService = null,
        PalworldUpdateOperationState? operationState = null,
        PalworldOptions? options = null)
    {
        return new PalworldUpdateService(
            Options.Create(options ?? CreateOptions()),
            restService ?? new RecordingRestService(new List<string>()),
            backupService ?? new RecordingBackupService(new List<string>()),
            containerService ?? new RecordingContainerService(new List<string>()),
            commandService ?? new RecordingCommandService(),
            updateRunner ?? new RecordingUpdateRunner(new List<string>()),
            notificationService ?? new RecordingNotificationService(),
            operationState ?? new PalworldUpdateOperationState(),
            NullLogger<PalworldUpdateService>.Instance);
    }

    private static PalworldOptions CreateOptions()
    {
        return new PalworldOptions
        {
            ContainerName = "palworld-server",
            Updates = new PalworldUpdateOptions
            {
                StartupVerificationTimeoutSeconds = 1,
                VerificationRetryDelayMilliseconds = 1
            }
        };
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public int NotificationCount { get; private set; }

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
            NotificationCount++;

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

        public int HealthFailuresBeforeSuccess { get; set; }

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

            if (_metricsCalls > 1 && HealthFailuresBeforeSuccess > 0)
            {
                HealthFailuresBeforeSuccess--;
                throw new PalworldRestUnavailableException("health not ready");
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
        private int _localManifestReads;

        public string? UpdateOnBootValue { get; set; } = "true";

        public bool FailEnvironmentInspection { get; set; }

        public string LocalManifest { get; set; } = "100";

        public string LocalManifestAfterUpdate { get; set; } = "200";

        public string RemoteAppInfoOutput { get; set; } = """
            "2394010"
            {
              "depots"
              {
                "2394012"
                {
                  "manifests"
                  {
                    "public"
                    {
                      "gid" "200"
                    }
                  }
                }
              }
            }
            """;

        public Task<PalworldContainerCommandResult> ExecuteAsync(
            string containerName,
            IReadOnlyList<string> command,
            CancellationToken cancellationToken)
        {
            var joinedCommand = string.Join(" ", command);

            if (joinedCommand.Contains("app_info_print", StringComparison.Ordinal))
            {
                return Task.FromResult(new PalworldContainerCommandResult(
                    0,
                    RemoteAppInfoOutput));
            }

            _localManifestReads++;

            return Task.FromResult(new PalworldContainerCommandResult(
                0,
                _localManifestReads == 1 ? LocalManifest : LocalManifestAfterUpdate));
        }

        public Task<string?> ReadEnvironmentVariableAsync(
            string containerName,
            string variableName,
            CancellationToken cancellationToken)
        {
            if (FailEnvironmentInspection)
            {
                throw new PalworldUpdateCommandException("inspect failed");
            }

            Assert.Equal("UPDATE_ON_BOOT", variableName);

            return Task.FromResult(UpdateOnBootValue);
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

        public int UpdateCount { get; private set; }

        public Task<string> PrepareUpdateAsync(
            string containerName,
            CancellationToken cancellationToken)
        {
            _events.Add("update");
            UpdateCount++;

            if (FailUpdate)
            {
                throw new PalworldUpdateCommandException("update failed");
            }

            return Task.FromResult("update-on-boot");
        }
    }
}
