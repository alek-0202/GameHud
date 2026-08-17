using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldAdminService
{
    Task<PalworldAdminActionResponse> AnnounceAsync(
        string serverId,
        PalworldAnnouncementRequest request,
        CancellationToken cancellationToken);

    Task<PalworldAdminActionResponse> KickAsync(
        string serverId,
        string userId,
        PalworldPlayerActionRequest request,
        CancellationToken cancellationToken);

    Task<PalworldAdminActionResponse> BanAsync(
        string serverId,
        string userId,
        PalworldPlayerActionRequest request,
        CancellationToken cancellationToken);

    Task<PalworldAdminActionResponse> UnbanAsync(
        string serverId,
        PalworldUnbanRequest request,
        CancellationToken cancellationToken);
}
