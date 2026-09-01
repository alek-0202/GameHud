using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.GameServers.Definitions;

public class GameDefinition
{
    public GameDefinition(
        GameId gameId,
        string displayName,
        string description,
        GameDefinitionBranding branding,
        IEnumerable<string> supportedRuntimes,
        IEnumerable<string> capabilities,
        GameRequirements? requirements = null,
        IEnumerable<GamePortDefinition>? ports = null,
        IEnumerable<GameStorageDefinition>? storages = null,
        IEnumerable<TrustedRuntimeImage>? runtimeImages = null)
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
        Requirements = requirements;
        Ports = NormalizePorts(ports);
        Storages = NormalizeStorages(storages);
        RuntimeImages = (runtimeImages ?? []).ToArray();
    }

    public GameId GameId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public GameDefinitionBranding Branding { get; }

    public IReadOnlyCollection<string> SupportedRuntimes { get; }

    public IReadOnlyCollection<string> Capabilities { get; }

    public GameRequirements? Requirements { get; }

    public IReadOnlyCollection<GamePortDefinition> Ports { get; }

    public IReadOnlyCollection<GameStorageDefinition> Storages { get; }

    public IReadOnlyCollection<TrustedRuntimeImage> RuntimeImages { get; }

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

    private static IReadOnlyCollection<GamePortDefinition> NormalizePorts(
        IEnumerable<GamePortDefinition>? ports)
    {
        if (ports is null)
        {
            return [];
        }

        var normalized = ports.ToArray();
        var duplicate = normalized
            .GroupBy(port => port.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate game port definition id '{duplicate.Key}'.",
                nameof(ports));
        }

        return normalized;
    }

    private static IReadOnlyCollection<GameStorageDefinition> NormalizeStorages(
        IEnumerable<GameStorageDefinition>? storages)
    {
        if (storages is null)
        {
            return [];
        }

        var normalized = storages.ToArray();
        var duplicate = normalized
            .GroupBy(storage => storage.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate game storage definition id '{duplicate.Key}'.",
                nameof(storages));
        }

        return normalized;
    }
}
