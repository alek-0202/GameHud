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

    private static readonly IReadOnlyList<string> ReadLocalManifestCommand =
    [
        "sh",
        "-lc",
        $"manifest=$(awk '/\"{PalworldUpdateConstants.ServerDepotId}\"/{{in_depot=1}} in_depot && /\"manifest\"/{{gsub(/\"/, \"\", $2); print $2; exit}}' {PalworldUpdateConstants.AppManifestPath} 2>/dev/null || true); if [ -n \"$manifest\" ]; then printf '%s\\n' \"$manifest\"; fi"
    ];

    private static readonly IReadOnlyList<string> ReadRemoteManifestCommand =
    [
        "sh",
        "-lc",
        $"if [ -x {PalworldUpdateConstants.PreferredSteamCmdPath} ]; then steamcmd={PalworldUpdateConstants.PreferredSteamCmdPath}; elif command -v steamcmd.sh >/dev/null 2>&1; then steamcmd=$(command -v steamcmd.sh); elif command -v steamcmd >/dev/null 2>&1; then steamcmd=$(command -v steamcmd); else echo 'SteamCMD executable was not found.' >&2; exit 127; fi; \"$steamcmd\" +login anonymous +app_info_update 1 +app_info_print {PalworldUpdateConstants.SteamAppId} +quit"
    ];

    private readonly IOptions<PalworldOptions> _options;
    private readonly IPalworldRestService _palworldRestService;
    private readonly IPalworldBackupService _backupService;
    private readonly IContainerService _containerService;
    private readonly IPalworldContainerCommandService _commandService;
    private readonly IPalworldUpdateRunner _updateRunner;
    private readonly INotificationService _notificationService;
    private readonly PalworldUpdateOperationState _operationState;
    private readonly ILogger<PalworldUpdateService> _logger;

    public PalworldUpdateService(
        IOptions<PalworldOptions> options,
        IPalworldRestService palworldRestService,
        IPalworldBackupService backupService,
        IContainerService containerService,
        IPalworldContainerCommandService commandService,
        IPalworldUpdateRunner updateRunner,
        INotificationService notificationService,
        PalworldUpdateOperationState operationState,
        ILogger<PalworldUpdateService> logger)
    {
        _options = options;
        _palworldRestService = palworldRestService;
        _backupService = backupService;
        _containerService = containerService;
        _commandService = commandService;
        _updateRunner = updateRunner;
        _notificationService = notificationService;
        _operationState = operationState;
        _logger = logger;
    }

    public async Task<PalworldUpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var checkedAt = DateTimeOffset.UtcNow;
        var installedVersion = await TryGetInstalledVersionAsync(cancellationToken);
        var localManifestId = await TryGetLocalManifestIdAsync(containerName, cancellationToken);
        var remoteManifestId = await TryGetRemoteManifestIdAsync(containerName, cancellationToken);
        var updateReadiness = await GetUpdateReadinessAsync(containerName, cancellationToken);

        if (remoteManifestId is null)
        {
            return new PalworldUpdateStatus(
                FormatInstalledVersion(installedVersion),
                null,
                null,
                null,
                PalworldUpdateStatuses.Unavailable,
                updateReadiness.Ready,
                updateReadiness.Status,
                updateReadiness.Message,
                checkedAt,
                PalworldUpdateConstants.Strategy,
                "Steam manifest information could not be checked from the configured Palworld container.");
        }

        if (localManifestId is null)
        {
            return new PalworldUpdateStatus(
                FormatInstalledVersion(installedVersion),
                null,
                FormatSteamManifest(remoteManifestId),
                remoteManifestId,
                PalworldUpdateStatuses.Unavailable,
                updateReadiness.Ready,
                updateReadiness.Status,
                updateReadiness.Message,
                checkedAt,
                PalworldUpdateConstants.Strategy,
                "Latest Steam manifest was found, but the installed Palworld manifest could not be read.");
        }

        var status = string.Equals(localManifestId, remoteManifestId, StringComparison.Ordinal)
            ? PalworldUpdateStatuses.UpToDate
            : PalworldUpdateStatuses.UpdateAvailable;

        return new PalworldUpdateStatus(
            FormatInstalledVersion(installedVersion),
            localManifestId,
            FormatSteamManifest(remoteManifestId),
            remoteManifestId,
            status,
            updateReadiness.Ready,
            updateReadiness.Status,
            updateReadiness.Message,
            checkedAt,
            PalworldUpdateConstants.Strategy,
            CreateUpdateStatusMessage(status, updateReadiness));
    }

    private static string CreateUpdateStatusMessage(
        string status,
        PalworldUpdateReadiness updateReadiness)
    {
        if (status == PalworldUpdateStatuses.UpdateAvailable && !updateReadiness.Ready)
        {
            return "A newer Palworld Steam manifest is available, but automatic updates are not configured for this server.";
        }

        return status == PalworldUpdateStatuses.UpdateAvailable
                ? "A newer Palworld Steam manifest appears to be available."
                : "Installed Palworld manifest matches the latest public Steam manifest.";
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

        using var operation = _operationState.TryBegin()
            ?? throw new PalworldUpdateConflictException("A Palworld update is already running.");

        var containerName = ResolveContainerName();
        var updateStatus = await CheckForUpdatesAsync(cancellationToken);

        if (!updateStatus.UpdateStatus.Equals(
            PalworldUpdateStatuses.UpdateAvailable,
            StringComparison.Ordinal))
        {
            throw new PalworldUpdateValidationException(
                "Palworld update can only be applied when GamesHud detects an available update.");
        }

        EnsureUpdateReady(updateStatus);

        var playersOnline = await TryGetPlayersOnlineAsync(cancellationToken);
        var announcementStatus = await TryAnnounceAsync(cancellationToken);
        var saveStatus = await SaveWorldForUpdateAsync(cancellationToken);
        var backup = await CreatePreUpdateBackupAsync(cancellationToken);
        var containerStopped = false;

        try
        {
            await StopConfiguredContainerAsync(containerName, cancellationToken);
            containerStopped = true;

            await PrepareUpdateAsync(containerName, cancellationToken);

            await StartConfiguredContainerAsync(containerName, cancellationToken);
            containerStopped = false;

            var healthCheckStatus = await CheckHealthAsync(containerName, cancellationToken);
            await GetVerifiedInstalledManifestAfterUpdateAsync(
                containerName,
                updateStatus,
                cancellationToken);
            var installedAfter = await TryGetInstalledVersionAsync(cancellationToken);

            var result = new PalworldUpdateResult(
                updateStatus.InstalledVersion,
                FormatInstalledVersion(installedAfter),
                updateStatus.AvailableVersion,
                true,
                playersOnline,
                announcementStatus,
                saveStatus,
                backup.Id,
                "stopped",
                PalworldUpdateStatuses.Applied,
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

    public static string? ExtractInstalledDepotManifestId(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var trimmedOutput = output.Trim();

        if (Regex.IsMatch(trimmedOutput, @"\A\d+\z", RegexOptions.CultureInvariant))
        {
            return trimmedOutput;
        }

        var depotBlock = ExtractVdfBlock(trimmedOutput, PalworldUpdateConstants.ServerDepotId);

        if (depotBlock is not null)
        {
            var depotManifest = Regex.Match(
                depotBlock,
                "\"manifest\"\\s+\"(?<manifestId>\\d+)\"",
                RegexOptions.CultureInvariant);

            if (depotManifest.Success)
            {
                return depotManifest.Groups["manifestId"].Value;
            }
        }

        return null;
    }

    public static string? ExtractLatestPublicDepotManifestId(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var depotBlock = ExtractVdfBlock(output, PalworldUpdateConstants.ServerDepotId);

        if (depotBlock is null)
        {
            return null;
        }

        var manifestsBlock = ExtractVdfBlock(depotBlock, "manifests") ?? depotBlock;
        var directPublicManifest = Regex.Match(
            manifestsBlock,
            "\"public\"\\s+\"(?<manifestId>\\d+)\"",
            RegexOptions.CultureInvariant);

        if (directPublicManifest.Success)
        {
            return directPublicManifest.Groups["manifestId"].Value;
        }

        var publicBlock = ExtractVdfBlock(manifestsBlock, "public");

        if (publicBlock is null)
        {
            return null;
        }

        var manifest = Regex.Match(
            publicBlock,
            "\"gid\"\\s+\"(?<manifestId>\\d+)\"",
            RegexOptions.CultureInvariant);

        return manifest.Success ? manifest.Groups["manifestId"].Value : null;
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

    private async Task<string?> TryGetLocalManifestIdAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.ExecuteAsync(
                containerName,
                ReadLocalManifestCommand,
                cancellationToken);

            return result.ExitCode == 0 ? ExtractInstalledDepotManifestId(result.Output) : null;
        }
        catch (PalworldUpdateException exception)
        {
            _logger.LogWarning(exception, "Unable to read installed Palworld Steam manifest id.");

            return null;
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker unavailable while reading Palworld Steam manifest id.");

            return null;
        }
    }

    private async Task<string?> TryGetRemoteManifestIdAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.ExecuteAsync(
                containerName,
                ReadRemoteManifestCommand,
                cancellationToken);

            return result.ExitCode == 0 ? ExtractLatestPublicDepotManifestId(result.Output) : null;
        }
        catch (PalworldUpdateException exception)
        {
            _logger.LogWarning(exception, "Unable to read latest Palworld Steam manifest id.");

            return null;
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker unavailable while reading latest Palworld Steam manifest id.");

            return null;
        }
    }

    private async Task<PalworldUpdateReadiness> GetUpdateReadinessAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await _commandService.ReadEnvironmentVariableAsync(
                containerName,
                "UPDATE_ON_BOOT",
                cancellationToken);

            if (value is null)
            {
                return new PalworldUpdateReadiness(
                    false,
                    PalworldUpdateReadinessStatuses.NotConfigured,
                    "UPDATE_ON_BOOT is not set on the configured Palworld container.");
            }

            if (value.Trim().Equals("true", StringComparison.Ordinal))
            {
                return new PalworldUpdateReadiness(
                    true,
                    PalworldUpdateReadinessStatuses.Ready,
                    "Automatic update-on-boot is configured on the current Palworld container.");
            }

            return new PalworldUpdateReadiness(
                false,
                PalworldUpdateReadinessStatuses.NotConfigured,
                "UPDATE_ON_BOOT is not enabled on the configured Palworld container.");
        }
        catch (Exception exception) when (exception is PalworldUpdateException or DockerUnavailableException)
        {
            _logger.LogWarning(exception, "Unable to verify UPDATE_ON_BOOT on the configured Palworld container.");

            return new PalworldUpdateReadiness(
                false,
                PalworldUpdateReadinessStatuses.Unavailable,
                "GamesHud could not verify whether automatic update-on-boot is configured on this server.");
        }
    }

    private static void EnsureUpdateReady(PalworldUpdateStatus updateStatus)
    {
        if (updateStatus.UpdateReady)
        {
            return;
        }

        throw new PalworldUpdateNotConfiguredException(
            "The configured Palworld container must have UPDATE_ON_BOOT=true before GamesHud can apply updates safely.");
    }

    private async Task<string> GetVerifiedInstalledManifestAfterUpdateAsync(
        string containerName,
        PalworldUpdateStatus updateStatus,
        CancellationToken cancellationToken)
    {
        var installedManifestAfter = await TryGetLocalManifestIdAsync(containerName, cancellationToken);

        if (string.IsNullOrWhiteSpace(updateStatus.AvailableBuild))
        {
            throw new PalworldUpdateFailedException(
                PalworldUpdateSteps.VersionCheck,
                "Palworld update could not be verified because the expected Steam manifest is unknown.");
        }

        if (string.Equals(installedManifestAfter, updateStatus.AvailableBuild, StringComparison.Ordinal))
        {
            return updateStatus.AvailableBuild;
        }

        throw new PalworldUpdateFailedException(
            PalworldUpdateSteps.VersionCheck,
            "Palworld container started, but the installed Steam manifest does not match the expected update.");
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
                ResolveUpdateOptions().LifecycleTimeoutSeconds,
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

    private PalworldUpdateOptions ResolveUpdateOptions()
    {
        var options = _options.Value.Updates;

        return new PalworldUpdateOptions
        {
            CommandTimeoutSeconds = Math.Clamp(options.CommandTimeoutSeconds, 5, 300),
            LifecycleTimeoutSeconds = Math.Clamp(options.LifecycleTimeoutSeconds, 1, 120)
        };
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

    private static string FormatInstalledVersion(string? installedVersion)
    {
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            return installedVersion;
        }

        return "Unknown";
    }

    private static string FormatSteamManifest(string manifestId)
    {
        return $"Steam manifest {manifestId}";
    }

    private static string? ExtractFirstBuildId(string output)
    {
        var match = Regex.Match(
            output,
            "\"buildid\"\\s+\"(?<buildId>\\d+)\"",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["buildId"].Value : null;
    }

    private static string? ExtractVdfBlock(string output, string key)
    {
        var keyIndex = output.IndexOf($"\"{key}\"", StringComparison.Ordinal);

        if (keyIndex < 0)
        {
            return null;
        }

        var blockStart = output.IndexOf('{', keyIndex);

        if (blockStart < 0)
        {
            return null;
        }

        var depth = 0;
        var inQuote = false;

        for (var index = blockStart; index < output.Length; index++)
        {
            var current = output[index];

            if (current == '"' && (index == 0 || output[index - 1] != '\\'))
            {
                inQuote = !inQuote;
            }

            if (inQuote)
            {
                continue;
            }

            if (current == '{')
            {
                depth++;
                continue;
            }

            if (current != '}')
            {
                continue;
            }

            depth--;

            if (depth == 0)
            {
                return output[(blockStart + 1)..index];
            }
        }

        return null;
    }
}
