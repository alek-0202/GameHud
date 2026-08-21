using GamesHud.Api.GameServers.Definitions;

namespace GamesHud.Api.GameServers.Services;

public sealed class PalworldGameServerPlugin : IGameServerPlugin
{
    private readonly PalworldGameDefinition _definition;

    public PalworldGameServerPlugin(PalworldGameDefinition definition)
    {
        _definition = definition;
    }

    public string GameType => _definition.GameId.ToString();

    public IReadOnlyCollection<string> Capabilities => _definition.Capabilities;
}
