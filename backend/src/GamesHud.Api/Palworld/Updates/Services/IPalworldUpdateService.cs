namespace GamesHud.Api.Palworld.Updates.Services;

public interface IPalworldUpdateService
{
    Task<PalworldUpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task<PalworldUpdateResult> ApplyUpdateAsync(
        string confirmationText,
        CancellationToken cancellationToken);
}
