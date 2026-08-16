using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldOverviewService
{
    Task<PalworldOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);

    Task<PalworldPlayersResponse> GetPlayersAsync(CancellationToken cancellationToken);
}
