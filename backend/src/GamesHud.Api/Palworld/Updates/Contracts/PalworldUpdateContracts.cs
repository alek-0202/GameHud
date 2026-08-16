namespace GamesHud.Api.Palworld.Updates.Contracts;

public sealed record PalworldUpdateStatusResponse(
    string? InstalledVersion,
    string? AvailableVersion,
    string UpdateStatus,
    string LastCheckedAt,
    string Strategy,
    string Message);

public sealed record PalworldUpdateRequest(
    string ConfirmationText);

public sealed record PalworldUpdateResponse(
    string Message,
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
    string CompletedAt);
