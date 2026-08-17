namespace GamesHud.Api.GameServers.Services;

public sealed class GameServerNotFoundException : Exception
{
    public GameServerNotFoundException(string serverId)
        : base($"Game server '{serverId}' was not found.")
    {
        ServerId = serverId;
    }

    public string ServerId { get; }
}
