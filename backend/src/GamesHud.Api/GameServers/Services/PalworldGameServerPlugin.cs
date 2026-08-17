namespace GamesHud.Api.GameServers.Services;

public sealed class PalworldGameServerPlugin : IGameServerPlugin
{
    public string GameType => "palworld";

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        GameServerCapabilities.Overview,
        GameServerCapabilities.Settings,
        GameServerCapabilities.Players,
        GameServerCapabilities.Backups,
        GameServerCapabilities.Update,
        GameServerCapabilities.Logs
    ];
}
