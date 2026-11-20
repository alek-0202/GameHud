using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Definitions;

public sealed class GameDefinitionRegistry : IGameDefinitionRegistry
{
    private readonly IReadOnlyDictionary<GameId, GameDefinition> _definitions;

    public GameDefinitionRegistry(IEnumerable<GameDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var definitionList = definitions.ToArray();
        var duplicateIds = definitionList
            .GroupBy(definition => definition.GameId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw new GameDefinitionConfigurationException(
                $"Duplicate game definition ids are not allowed: {string.Join(", ", duplicateIds)}.");
        }

        _definitions = definitionList.ToDictionary(definition => definition.GameId);
    }

    public IReadOnlyCollection<GameDefinition> GetAll()
    {
        return _definitions.Values
            .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public GameDefinition Get(GameId gameId)
    {
        return TryGet(gameId, out var definition)
            ? definition!
            : throw new GameDefinitionNotFoundException(gameId);
    }

    public bool TryGet(GameId gameId, out GameDefinition? definition)
    {
        return _definitions.TryGetValue(gameId, out definition);
    }
}
