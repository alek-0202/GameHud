using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldOverviewService
{
    Task<PalworldOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);

    Task<PalworldOverviewResponse> GetOverviewAsync(
        string? serverId,
        CancellationToken cancellationToken)
    {
        return GetOverviewAsync(cancellationToken);
    }

    Task<PalworldPlayersResponse> GetPlayersAsync(CancellationToken cancellationToken);

    Task<PalworldPlayersResponse> GetPlayersAsync(
        string? serverId,
        CancellationToken cancellationToken)
    {
        return GetPlayersAsync(cancellationToken);
    }
}
