using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Definitions;

public interface IGameDefinitionRegistry
{
    IReadOnlyCollection<GameDefinition> GetAll();

    GameDefinition Get(GameId gameId);

    bool TryGet(GameId gameId, out GameDefinition? definition);
}
