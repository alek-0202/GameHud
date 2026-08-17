using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldConfigService
{
    Task<PalworldConfigResponse> GetConfigAsync(CancellationToken cancellationToken);

    Task<PalworldConfigResponse> GetConfigAsync(
        string? serverId,
        CancellationToken cancellationToken)
    {
        return GetConfigAsync(cancellationToken);
    }

    Task<PalworldConfigUpdateResponse> UpdateConfigAsync(
        PalworldConfigUpdateRequest request,
        bool restart,
        CancellationToken cancellationToken);

    Task<PalworldConfigUpdateResponse> UpdateConfigAsync(
        string? serverId,
        PalworldConfigUpdateRequest request,
        bool restart,
        CancellationToken cancellationToken)
    {
        return UpdateConfigAsync(request, restart, cancellationToken);
    }
}
