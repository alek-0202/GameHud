namespace GamesHud.Api.Palworld.Services;

public interface IPalworldRestService
{
    Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken);

    Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken);

    Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken);

    Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken);

    Task SaveWorldAsync(CancellationToken cancellationToken);

    Task AnnounceAsync(
        string message,
        CancellationToken cancellationToken);
}
