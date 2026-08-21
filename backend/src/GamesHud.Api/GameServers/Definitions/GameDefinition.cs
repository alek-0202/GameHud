using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Definitions;

public class GameDefinition
{
    public GameDefinition(
        GameId gameId,
        string displayName,
        string description,
        GameDefinitionBranding branding,
        IEnumerable<string> supportedRuntimes,
        IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(gameId.Value))
        {
            throw new ArgumentException("Game id is required.", nameof(gameId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(supportedRuntimes);
        ArgumentNullException.ThrowIfNull(capabilities);

        GameId = gameId;
        DisplayName = displayName.Trim();
        Description = description.Trim();
        Branding = branding;
        SupportedRuntimes = NormalizeIdentifiers(supportedRuntimes, nameof(supportedRuntimes));
        Capabilities = NormalizeIdentifiers(capabilities, nameof(capabilities));
    }

    public GameId GameId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public GameDefinitionBranding Branding { get; }

    public IReadOnlyCollection<string> SupportedRuntimes { get; }

    public IReadOnlyCollection<string> Capabilities { get; }

    private static IReadOnlyCollection<string> NormalizeIdentifiers(
        IEnumerable<string> values,
        string parameterName)
    {
        var normalized = values
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Identifiers cannot be empty.", parameterName)
                : value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0
            ? throw new ArgumentException("At least one identifier is required.", parameterName)
            : normalized;
    }
}
