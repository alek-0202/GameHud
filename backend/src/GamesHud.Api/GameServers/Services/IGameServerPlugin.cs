namespace GamesHud.Api.GameServers.Services;

public interface IGameServerPlugin
{
    string GameType { get; }

    IReadOnlyCollection<string> Capabilities { get; }
}
