using GamesHud.Api.Palworld.Configuration;

namespace GamesHud.Api.GameServers.Services;

public interface IGameServerRegistry
{
    IReadOnlyCollection<GameServerDescriptor> GetServers();

    GameServerDescriptor GetServer(string serverId);

    PalworldOptions GetPalworldOptions(string? serverId = null);
}
