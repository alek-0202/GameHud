using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldModsService
{
    Task<PalworldModsResponse> GetModsAsync(
        string serverId,
        CancellationToken cancellationToken);
}
