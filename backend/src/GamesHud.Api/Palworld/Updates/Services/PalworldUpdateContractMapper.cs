using GamesHud.Api.Palworld.Updates.Contracts;

namespace GamesHud.Api.Palworld.Updates.Services;

public static class PalworldUpdateContractMapper
{
    public static PalworldUpdateStatusResponse Map(PalworldUpdateStatus status)
    {
        return new PalworldUpdateStatusResponse(
            status.InstalledVersion,
            status.AvailableVersion,
            status.UpdateStatus,
            status.LastCheckedAt.ToString("O"),
            status.Strategy,
            status.Message);
    }

    public static PalworldUpdateResponse Map(PalworldUpdateResult result)
    {
        return new PalworldUpdateResponse(
            "Palworld update flow completed.",
            result.InstalledVersionBefore,
            result.InstalledVersionAfter,
            result.AvailableVersion,
            result.UpdateApplied,
            result.PlayersOnlineBeforeUpdate,
            result.AnnouncementStatus,
            result.SaveStatus,
            result.BackupId,
            result.StopStatus,
            result.UpdateStatus,
            result.StartStatus,
            result.HealthCheckStatus,
            result.CompletedAt.ToString("O"));
    }
}
