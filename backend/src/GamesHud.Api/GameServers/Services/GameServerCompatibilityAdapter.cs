using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Services;

internal static class GameServerCompatibilityAdapter
{
    public static GameServer ToDomain(GameServerOptions server)
    {
        return new GameServer(
            new GameServerId(server.Id),
            new GameId(server.Type),
            ResolveDisplayName(server),
            new GameServerRuntime(GameServerRuntime.DockerType, server.ContainerName),
            new GameServerInstallation(GameServerInstallationType.LegacyExternal));
    }

    private static string ResolveDisplayName(GameServerOptions server)
    {
        if (!string.IsNullOrWhiteSpace(server.DisplayName))
        {
            return server.DisplayName.Trim();
        }

        return server.Type.Equals("palworld", StringComparison.OrdinalIgnoreCase)
            ? "Palworld"
            : server.Id;
    }
}
