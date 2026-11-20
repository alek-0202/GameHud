using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Palworld.Configuration;

namespace GamesHud.Api.GameServers.Services;

public interface IGameServerRegistry
{
    IReadOnlyCollection<GameServerDescriptor> GetServers();

    GameServerDescriptor GetServer(string serverId);

    IReadOnlyCollection<GameServer> GetGameServers();

    GameServer GetGameServer(GameServerId serverId);

    PalworldOptions GetPalworldOptions(string? serverId = null);
}
