using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Services;

namespace GamesHud.Api.GameServers.Definitions;

public sealed class PalworldGameDefinition : GameDefinition
{
    public PalworldGameDefinition()
        : base(
            new GameId("palworld"),
            "Palworld",
            "Operate Palworld dedicated servers with supported management tools.",
            new GameDefinitionBranding("palworld"),
            [GameServerRuntime.DockerType],
            [
                GameServerCapabilities.Overview,
                GameServerCapabilities.Settings,
                GameServerCapabilities.Players,
                GameServerCapabilities.Backups,
                GameServerCapabilities.Update,
                GameServerCapabilities.Logs,
                GameServerCapabilities.PlayerManagement,
                GameServerCapabilities.Mods
            ])
    {
    }
}
