using System.Text.RegularExpressions;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Updates.Services;

public sealed class PalworldUpdateService : IPalworldUpdateService
{
    public const string UpdateConfirmation = "UPDATE PALWORLD SERVER";

    private const string Strategy = "thijsvanloef-update-on-boot-steamcmd";
    private const int LifecycleTimeoutSeconds = 30;

    private static readonly IReadOnlyList<string> ReadUpdateOnBootCommand =
    [
        "sh",
        "-lc",
        "case \"${UPDATE_ON_BOOT:-}\" in true|TRUE|1) echo true ;; *) echo false ;; esac"
    ];

    private static readonly IReadOnlyList<string> ReadLocalBuildCommand =
    [
        "sh",
        "-lc",
        "grep -m1 '\"buildid\"' /palworld/steamapps/appmanifest_2394010.acf 2>/dev/null || true"
    ];

    private static readonly IReadOnlyList<string> ReadRemoteBuildCommand =
    [
        "sh",
        "-lc",
        "steamcmd +login anonymous +app_info_update 1 +app_info_print 2394010 +quit"
    ];

    private readonly IOptions<PalworldOptions> _options;
    private readonly IPalworldRestService _palworldRestService;
    private readonly IPalworldBackupService _backupService;
    private readonly IContainerService _containerService;
    private readonly IPalworldContainerCommandService _commandService;
    private readonly IPalworldUpdateRunner _updateRunner;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PalworldUpdateService> _logger;

    public PalworldUpdateService(
        IOptions<PalworldOptions> options,
        IPalworldRestService palworldRestService,
        IPalworldBackupService backupService,
        IContainerService containerService,
        IPalworldContainerCommandService commandService,
        IPalworldUpdateRunner updateRunner,
        INotificationService notificationService,
        ILogger<PalworldUpdateService> logger)
    {
        _options = options;
        _palworldRestService = palworldRestService;
        _backupService = backupService;
        _containerService = containerService;
        _commandService = commandService;
        _updateRunner = updateRunner;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<PalworldUpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var checkedAt = DateTimeOffset.UtcNow;
        var installedVersion = await TryGetInstalledVersionAsync(cancellationToken);
        var localBuildId = await TryGetLocalBuildIdAsync(containerName, cancellationToken);
        var remoteBuildId = await TryGetRemoteBuildIdAsync(containerName, cancellationToken);

        if (remoteBuildId is null)
        {
            return new PalworldUpdateStatus(
                FormatInstalledVersion(installedVersion, localBuildId),
                null,
                PalworldUpdateStatuses.CheckUnavailable,
                checkedAt,
                Strategy,
                "Steam build information could not be checked from the configured Palworld container.");
        }

        if (localBuildId is null)
        {
            return new PalworldUpdateStatus(
                FormatInstalledVersion(installedVersion, null),
                FormatSteamBuild(remoteBuildId),
                PalworldUpdateStatuses.Unknown,
                checkedAt,
                Strategy,
                "Latest Steam build was found, but the installed build id could not be read.");
        }

        var status = string.Equals(localBuildId, remoteBuildId, StringComparison.Ordinal)
            ? PalworldUpdateStatuses.UpToDate
            : PalworldUpdateStatuses.UpdateAvailable;

        var response = new PalworldUpdateStatus(
            FormatInstalledVersion(installedVersion, localBuildId),
            FormatSteamBuild(remoteBuildId),
            status,
            checkedAt,
            Strategy,
            status == PalworldUpdateStatuses.UpdateAvailable
                ? "A newer Steam build appears to be available."
                : "Installed Steam build matches the latest public Steam build.");

        if (status == PalworldUpdateStatuses.UpdateAvailable)
        {
            await _notificationService.NotifyAsync(
                new NotificationEvent(
                    NotificationEventTypes.UpdateAvailable,
                    "Palworld update available",
                    "A newer Palworld Steam build appears to be available.",
                    "palworld-update-available"),
                cancellationToken);
        }

        return response;
    }

    public async Task<PalworldUpdateResult> ApplyUpdateAsync(
        string confirmationText,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmationText, UpdateConfirmation, StringComparison.Ordinal))
        {
            throw new PalworldUpdateValidationException(
                $"Confirmation text must be exactly '{UpdateConfirmation}'.");
        }

        var containerName = ResolveContainerName();
        var updateStatus = await CheckForUpdatesAsync(cancellationToken);

        if (!updateStatus.UpdateStatus.Equals(
            PalworldUpdateStatuses.UpdateAvailable,
            StringComparison.Ordinal))
        {
            throw new PalworldUpdateValidationException(
                "Palworld update can only be applied when GamesHud detects an available update.");
        }

        await EnsureUpdateOnBootEnabledAsync(containerName, cancellationToken);

        var playersOnline = await TryGetPlayersOnlineAsync(cancellationToken);
        var announcementStatus = await TryAnnounceAsync(cancellationToken);
        var saveStatus = await SaveWorldForUpdateAsync(cancellationToken);
        var backup = await CreatePreUpdateBackupAsync(cancellationToken);
        var containerStopped = false;

        try
        {
            await StopConfiguredContainerAsync(containerName, cancellationToken);
            containerStopped = true;

            var preparedUpdateStatus = await PrepareUpdateAsync(containerName, cancellationToken);

            await StartConfiguredContainerAsync(containerName, cancellationToken);
            containerStopped = false;

            var healthCheckStatus = await CheckHealthAsync(containerName, cancellationToken);
            var installedAfter = await TryGetInstalledVersionAsync(cancellationToken);
            var finalStatus = ResolveFinalUpdateStatus(
                updateStatus.InstalledVersion,
                installedAfter,
                preparedUpdateStatus);

            var result = new PalworldUpdateResult(
                updateStatus.InstalledVersion,
                FormatInstalledVersion(installedAfter, await TryGetLocalBuildIdAsync(containerName, cancellationToken)),
                updateStatus.AvailableVersion,
                true,
                playersOnline,
                announcementStatus,
                saveStatus,
                backup.Id,
                "stopped",
                finalStatus,
                "started",
                healthCheckStatus,
                DateTimeOffset.UtcNow);

            await _notificationService.NotifyAsync(
                new NotificationEvent(
                    NotificationEventTypes.UpdateCompleted,
                    "Palworld update completed",
                    "Manual Palworld update flow completed.",
                    "palworld-update-completed"),
                cancellationToken);

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Palworld update flow failed.");

            if (containerStopped)
            {
                await TryStartContainerAfterUpdateFailureAsync(containerName, cancellationToken);
            }

            if (exception is PalworldUpdateFailedException)
            {
                throw;
            }

            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Update,
                "Palworld update flow failed. GamesHud attempted to restore service when safe.",
                exception);
        }
    }

    public static string? ExtractSteamBuildId(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var publicBranchMatch = Regex.Match(
            output,
            "\"public\"\\s*\\{(?<branch>.*?)\\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        if (publicBranchMatch.Success)
        {
            var branchBuild = ExtractFirstBuildId(publicBranchMatch.Groups["branch"].Value);

            if (branchBuild is not null)
            {
                return branchBuild;
            }
        }

        return ExtractFirstBuildId(output);
    }

    private async Task<string?> TryGetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await _palworldRestService.GetInfoAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(info.Version)
                ? null
                : info.Version.Trim();
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Unable to read installed Palworld version from REST API.");

            return null;
        }
    }

    private async Task<string?> TryGetLocalBuildIdAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.ExecuteAsync(
                containerName,
                ReadLocalBuildCommand,
                cancellationToken);

            return result.ExitCode == 0 ? ExtractSteamBuildId(result.Output) : null;
        }
        catch (PalworldUpdateException exception)
        {
            _logger.LogWarning(exception, "Unable to read installed Palworld Steam build id.");

            return null;
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker unavailable while reading Palworld Steam build id.");

            return null;
        }
    }

    private async Task<string?> TryGetRemoteBuildIdAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.ExecuteAsync(
                containerName,
                ReadRemoteBuildCommand,
                cancellationToken);

            return result.ExitCode == 0 ? ExtractSteamBuildId(result.Output) : null;
        }
        catch (PalworldUpdateException exception)
        {
            _logger.LogWarning(exception, "Unable to read latest Palworld Steam build id.");

            return null;
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker unavailable while reading latest Palworld Steam build id.");

            return null;
        }
    }

    private async Task EnsureUpdateOnBootEnabledAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await _commandService.ExecuteAsync(
            containerName,
            ReadUpdateOnBootCommand,
            cancellationToken);

        if (result.ExitCode != 0
            || !result.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new PalworldUpdateValidationException(
                "The configured Palworld container must have UPDATE_ON_BOOT=true before GamesHud can apply updates safely.");
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
            _logger.LogWarning(exception, "Unable to read players online before Palworld update.");

            return null;
        }
    }

    private async Task<string> TryAnnounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _palworldRestService.AnnounceAsync(
                "Server update will start shortly. Please disconnect safely.",
                cancellationToken);

            return "announced";
        }
        catch (PalworldRestConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld REST API is not configured for update announcement.");

            return "unavailable";
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Palworld update announcement failed.");

            return "failed";
        }
    }

    private async Task<string> SaveWorldForUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _palworldRestService.SaveWorldAsync(cancellationToken);

            return "saved";
        }
        catch (Exception exception) when (exception is PalworldRestException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Save,
                "Palworld world save failed. Update was not started.",
                exception);
        }
    }

    private async Task<PalworldBackupMetadata> CreatePreUpdateBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _backupService.CreateBackupAsync(
                new PalworldBackupCreateOptions(
                    PalworldBackupTypes.PreUpdate,
                    "Automatic pre-update backup.",
                    RequestWorldSave: false),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Backup,
                "Palworld pre-update backup failed. Update was not started.",
                exception);
        }
    }

    private async Task StopConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _containerService.StopContainerAsync(
                containerName,
                LifecycleTimeoutSeconds,
                cancellationToken);

            EnsureLifecycleSuccess(result, "stopped", "stop");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Stop,
                "Configured Palworld container could not be stopped.",
                exception);
        }
    }

    private async Task<string> PrepareUpdateAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _updateRunner.PrepareUpdateAsync(containerName, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Update,
                "Palworld update could not be prepared.",
                exception);
        }
    }

    private async Task StartConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _containerService.StartContainerAsync(containerName, cancellationToken);

            EnsureLifecycleSuccess(result, "started", "start");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Start,
                "Configured Palworld container could not be started after update.",
                exception);
        }
    }

    private async Task<string> CheckHealthAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var container = await _containerService.GetContainerDetailsAsync(containerName, cancellationToken);

            if (container is null)
            {
                throw new PalworldUpdateLifecycleException("Configured Palworld container was not found.");
            }

            if (!container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                throw new PalworldUpdateLifecycleException("Configured Palworld container is not running after update.");
            }

            await _palworldRestService.GetMetricsAsync(cancellationToken);

            return "healthy";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.Health,
                "Palworld health check failed after update.",
                exception);
        }
    }

    private async Task TryStartContainerAfterUpdateFailureAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _containerService.StartContainerAsync(containerName, cancellationToken);

            if (result?.Success != true)
            {
                _logger.LogError("Unable to start configured Palworld container after update failure.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unable to start configured Palworld container after update failure.");
        }
    }

    private string ResolveContainerName()
    {
        var containerName = _options.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new PalworldUpdateConfigurationException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }

    private static void EnsureLifecycleSuccess(
        ContainerLifecycleActionResponse? response,
        string expectedState,
        string action)
    {
        if (response is null)
        {
            throw new PalworldUpdateLifecycleException("Configured Palworld container was not found.");
        }

        if (!response.Success)
        {
            throw new PalworldUpdateLifecycleException(
                $"Configured Palworld container {action} action failed.");
        }

        if (IsExpectedLifecycleState(response.CurrentState, expectedState))
        {
            return;
        }

        throw new PalworldUpdateLifecycleException(
            $"Configured Palworld container was not {expectedState}.");
    }

    private static bool IsExpectedLifecycleState(string currentState, string expectedState)
    {
        if (expectedState.Equals("started", StringComparison.Ordinal))
        {
            return currentState.Equals("running", StringComparison.OrdinalIgnoreCase);
        }

        if (expectedState.Equals("stopped", StringComparison.Ordinal))
        {
            return currentState.Equals("exited", StringComparison.OrdinalIgnoreCase)
                || currentState.Equals("stopped", StringComparison.OrdinalIgnoreCase)
                || currentState.Equals("created", StringComparison.OrdinalIgnoreCase);
        }

        return currentState.Equals(expectedState, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFinalUpdateStatus(
        string? installedBefore,
        string? installedAfter,
        string preparedUpdateStatus)
    {
        if (!string.IsNullOrWhiteSpace(installedBefore)
            && !string.IsNullOrWhiteSpace(installedAfter)
            && !installedBefore.Equals(installedAfter, StringComparison.OrdinalIgnoreCase))
        {
            return PalworldUpdateStatuses.Applied;
        }

        return preparedUpdateStatus.Equals("update-on-boot", StringComparison.Ordinal)
            ? PalworldUpdateStatuses.AppliedVersionUnknown
            : preparedUpdateStatus;
    }

    private static string FormatInstalledVersion(
        string? installedVersion,
        string? localBuildId)
    {
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            return installedVersion;
        }

        return localBuildId is null ? "Unknown" : FormatSteamBuild(localBuildId);
    }

    private static string FormatSteamBuild(string buildId)
    {
        return $"Steam build {buildId}";
    }

    private static string? ExtractFirstBuildId(string output)
    {
        var match = Regex.Match(
            output,
            "\"buildid\"\\s+\"(?<buildId>\\d+)\"",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["buildId"].Value : null;
    }
}
