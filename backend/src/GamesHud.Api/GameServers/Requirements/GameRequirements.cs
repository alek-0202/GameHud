using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Requirements;

public sealed record GameRequirements
{
    public GameRequirements(
        IEnumerable<string> supportedOperatingSystems,
        IEnumerable<string> supportedArchitectures,
        IEnumerable<string> requiredRuntimes,
        int? minimumLogicalProcessors = null,
        int? recommendedLogicalProcessors = null,
        ByteRequirement? memory = null,
        ByteRequirement? storage = null,
        string? source = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(supportedOperatingSystems);
        ArgumentNullException.ThrowIfNull(supportedArchitectures);
        ArgumentNullException.ThrowIfNull(requiredRuntimes);

        SupportedOperatingSystems = NormalizeIdentifiers(supportedOperatingSystems);
        SupportedArchitectures = NormalizeIdentifiers(supportedArchitectures);
        RequiredRuntimes = NormalizeIdentifiers(requiredRuntimes);
        MinimumLogicalProcessors = minimumLogicalProcessors;
        RecommendedLogicalProcessors = recommendedLogicalProcessors;
        Memory = memory;
        Storage = storage;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (minimumLogicalProcessors is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLogicalProcessors));
        }

        if (recommendedLogicalProcessors is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recommendedLogicalProcessors));
        }
    }

    public IReadOnlyCollection<string> SupportedOperatingSystems { get; }

    public IReadOnlyCollection<string> SupportedArchitectures { get; }

    public IReadOnlyCollection<string> RequiredRuntimes { get; }

    public int? MinimumLogicalProcessors { get; }

    public int? RecommendedLogicalProcessors { get; }

    public ByteRequirement? Memory { get; }

    public ByteRequirement? Storage { get; }

    public string? Source { get; }

    public string? Notes { get; }

    private static IReadOnlyCollection<string> NormalizeIdentifiers(IEnumerable<string> values)
    {
        return values
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Requirement identifiers cannot be empty.", nameof(values))
                : value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record ByteRequirement
{
    public ByteRequirement(ulong? minimumBytes = null, ulong? recommendedBytes = null)
    {
        if (minimumBytes is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumBytes));
        }

        if (recommendedBytes is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recommendedBytes));
        }

        MinimumBytes = minimumBytes;
        RecommendedBytes = recommendedBytes;
    }

    public ulong? MinimumBytes { get; }

    public ulong? RecommendedBytes { get; }
}

public static class GameRequirementBytes
{
    public static ulong Gibibytes(ulong value)
    {
        return checked(value * 1024 * 1024 * 1024);
    }
}

public interface IGameRequirementsDefinition
{
    GameRequirements Requirements { get; }
}
