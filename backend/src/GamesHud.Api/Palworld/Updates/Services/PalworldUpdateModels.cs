namespace GamesHud.Api.Palworld.Updates.Services;

public static class PalworldUpdateStatuses
{
    public const string Unknown = "unknown";
    public const string UpToDate = "up-to-date";
    public const string UpdateAvailable = "update-available";
    public const string CheckUnavailable = "check-unavailable";
    public const string Applied = "applied";
    public const string AppliedVersionUnknown = "applied-version-unknown";
}

public static class PalworldUpdateSteps
{
    public const string Announce = "announce";
    public const string Save = "save";
    public const string Backup = "backup";
    public const string Stop = "stop";
    public const string Update = "update";
    public const string Start = "start";
    public const string Health = "health";
    public const string VersionCheck = "version-check";
}

public sealed record PalworldUpdateStatus(
    string? InstalledVersion,
    string? AvailableVersion,
    string UpdateStatus,
    DateTimeOffset LastCheckedAt,
    string Strategy,
    string Message);

public sealed record PalworldUpdateResult(
    string? InstalledVersionBefore,
    string? InstalledVersionAfter,
    string? AvailableVersion,
    bool UpdateApplied,
    int? PlayersOnlineBeforeUpdate,
    string AnnouncementStatus,
    string SaveStatus,
    string BackupId,
    string StopStatus,
    string UpdateStatus,
    string StartStatus,
    string HealthCheckStatus,
    DateTimeOffset CompletedAt);

public sealed record PalworldContainerCommandResult(
    int ExitCode,
    string Output);
