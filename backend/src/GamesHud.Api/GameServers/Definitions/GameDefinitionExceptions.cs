using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Definitions;

public sealed class GameDefinitionConfigurationException : Exception
{
    public GameDefinitionConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class GameDefinitionNotFoundException : Exception
{
    public GameDefinitionNotFoundException(GameId gameId)
        : base($"Game definition '{gameId}' was not found.")
    {
        GameId = gameId;
    }

    public GameId GameId { get; }
}
