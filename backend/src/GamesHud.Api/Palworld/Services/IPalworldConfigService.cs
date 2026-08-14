using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldConfigService
{
    Task<PalworldConfigResponse> GetConfigAsync(CancellationToken cancellationToken);

    Task<PalworldConfigUpdateResponse> UpdateConfigAsync(
        PalworldConfigUpdateRequest request,
        bool restart,
        CancellationToken cancellationToken);
}
