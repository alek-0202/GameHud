namespace GamesHud.Api.GameServers.Services;

public sealed class GameServerConfigurationException : Exception
{
    public GameServerConfigurationException(string message)
        : base(message)
    {
    }
}
