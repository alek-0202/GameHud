using GamesHud.Api.Docker.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using GamesHud.Api.Palworld.Updates.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Operations.Scheduling;

public sealed class ScheduledOperationExecutor : IScheduledOperationExecutor
{
    private readonly IPalworldBackupService _backupService;
    private readonly IPalworldRestService _palworldRestService;
    private readonly IPalworldUpdateService _updateService;
    private readonly IContainerService _containerService;
    private readonly INotificationService _notificationService;
    private readonly IOptions<PalworldOptions> _palworldOptions;
    private readonly IOptions<ScheduledOperationOptions> _operationOptions;

    public ScheduledOperationExecutor(
        IPalworldBackupService backupService,
        IPalworldRestService palworldRestService,
        IPalworldUpdateService updateService,
        IContainerService containerService,
        INotificationService notificationService,
        IOptions<PalworldOptions> palworldOptions,
        IOptions<ScheduledOperationOptions> operationOptions)
    {
        _backupService = backupService;
        _palworldRestService = palworldRestService;
        _updateService = updateService;
        _containerService = containerService;
        _notificationService = notificationService;
        _palworldOptions = palworldOptions;
        _operationOptions = operationOptions;
    }

    public async Task<ScheduledOperationResult> ExecuteAsync(
        ScheduleTask task,
        CancellationToken cancellationToken)
    {
        return task.ActionType switch
        {
            ScheduleActionTypes.AutomaticBackup => await CreateAutomaticBackupAsync(cancellationToken),
            ScheduleActionTypes.RestartPalworld => await RestartPalworldAsync(task, cancellationToken),
            ScheduleActionTypes.UpdateCheck => await CheckUpdateAsync(cancellationToken),
            ScheduleActionTypes.Announcement => await SendAnnouncementAsync(task, cancellationToken),
            ScheduleActionTypes.ShutdownPalworld => await ShutdownPalworldAsync(task, cancellationToken),
            _ => new ScheduledOperationResult(false, "unsupported-action")
        };
    }

    private async Task<ScheduledOperationResult> CreateAutomaticBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _backupService.CreateBackupAsync(
                new PalworldBackupCreateOptions(
                    PalworldBackupTypes.Automatic,
                    "Automatic scheduled backup.",
                    RequestWorldSave: true),
                cancellationToken);
            await _notificationService.NotifyAsync(
                new NotificationEvent(
                    NotificationEventTypes.BackupCompleted,
                    "Palworld backup completed",
                    "Scheduled Palworld backup completed.",
                    "palworld-backup-completed"),
                cancellationToken);

            return new ScheduledOperationResult(true, "backup-completed");
        }
        catch
        {
            await _notificationService.NotifyAsync(
                new NotificationEvent(
                    NotificationEventTypes.BackupFailed,
                    "Palworld backup failed",
                    "Scheduled Palworld backup failed.",
                    "palworld-backup-failed"),
                cancellationToken);

            throw;
        }
    }

    private async Task<ScheduledOperationResult> RestartPalworldAsync(
        ScheduleTask task,
        CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var options = ResolvePalworldScheduleOptions();

        foreach (var warningMinutes in options.RestartWarningMinutes.OrderByDescending(value => value))
        {
            await _palworldRestService.AnnounceAsync(
                $"Restart in {warningMinutes} minute{(warningMinutes == 1 ? string.Empty : "s")}.",
                cancellationToken);
        }

        await _palworldRestService.AnnounceAsync(
            task.Message ?? "Scheduled restart is starting.",
            cancellationToken);
        await _palworldRestService.SaveWorldAsync(cancellationToken);

        if (options.RestartWaitSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.RestartWaitSeconds), cancellationToken);
        }

        var result = await _containerService.RestartContainerAsync(
            containerName,
            options.LifecycleTimeoutSeconds,
            cancellationToken);

        if (result is null || !result.Success)
        {
            return new ScheduledOperationResult(false, "restart-failed");
        }

        await _notificationService.NotifyAsync(
            new NotificationEvent(
                NotificationEventTypes.RestartCompleted,
                "Palworld restart completed",
                "Scheduled Palworld restart completed.",
                "palworld-restart-completed"),
            cancellationToken);

        return new ScheduledOperationResult(true, "restart-completed");
    }

    private async Task<ScheduledOperationResult> CheckUpdateAsync(CancellationToken cancellationToken)
    {
        var status = await _updateService.CheckForUpdatesAsync(cancellationToken);

        return new ScheduledOperationResult(true, status.UpdateStatus);
    }

    private async Task<ScheduledOperationResult> SendAnnouncementAsync(
        ScheduleTask task,
        CancellationToken cancellationToken)
    {
        await _palworldRestService.AnnounceAsync(
            task.Message ?? "GamesHud scheduled announcement.",
            cancellationToken);

        return new ScheduledOperationResult(true, "announcement-sent");
    }

    private async Task<ScheduledOperationResult> ShutdownPalworldAsync(
        ScheduleTask task,
        CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var options = ResolvePalworldScheduleOptions();

        await _palworldRestService.AnnounceAsync(
            task.Message ?? "Scheduled shutdown is starting.",
            cancellationToken);
        await _palworldRestService.SaveWorldAsync(cancellationToken);

        if (options.RestartWaitSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.RestartWaitSeconds), cancellationToken);
        }

        var result = await _containerService.StopContainerAsync(
            containerName,
            options.LifecycleTimeoutSeconds,
            cancellationToken);

        if (result is null || !result.Success)
        {
            return new ScheduledOperationResult(false, "shutdown-failed");
        }

        await _notificationService.NotifyAsync(
            new NotificationEvent(
                NotificationEventTypes.ServerStopped,
                "Palworld stopped",
                "Scheduled Palworld shutdown completed.",
                "palworld-server-stopped"),
            cancellationToken);

        return new ScheduledOperationResult(true, "shutdown-completed");
    }

    private string ResolveContainerName()
    {
        var containerName = _palworldOptions.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new SchedulerValidationException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }

    private PalworldScheduledOperationOptions ResolvePalworldScheduleOptions()
    {
        var options = _operationOptions.Value.Palworld;

        return new PalworldScheduledOperationOptions
        {
            LifecycleTimeoutSeconds = Math.Clamp(options.LifecycleTimeoutSeconds, 1, 120),
            RestartWaitSeconds = Math.Clamp(options.RestartWaitSeconds, 0, 600),
            RestartWarningMinutes = options.RestartWarningMinutes
                .Where(value => value is >= 1 and <= 60)
                .DefaultIfEmpty(1)
                .Distinct()
                .ToArray()
        };
    }
}
