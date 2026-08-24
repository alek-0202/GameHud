using GamesHud.Api.GameServers.Domain;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Storage;

public interface IManagedStoragePathBuilder
{
    ManagedStorageLayout CreateLayout(GameServerId gameServerId);
}

public sealed record ManagedStorageLayout(
    string DataRoot,
    string ServerRoot,
    string ServerRelativePath);

public sealed class ManagedStoragePathBuilder : IManagedStoragePathBuilder
{
    private readonly IOptions<StorageOptions> _options;

    public ManagedStoragePathBuilder(IOptions<StorageOptions> options)
    {
        _options = options;
    }

    public ManagedStorageLayout CreateLayout(GameServerId gameServerId)
    {
        var dataRoot = ResolveDataRoot(_options.Value.DataRoot);
        var serverSegment = CreateSafeServerSegment(gameServerId);
        var serverRelativePath = Path.Combine("servers", serverSegment);
        var serverRoot = EnsureContained(
            dataRoot,
            Path.Combine(dataRoot, serverRelativePath),
            "Planned server path escaped the managed data root.");

        return new ManagedStorageLayout(dataRoot, serverRoot, serverRelativePath);
    }

    public static string CreateSafeServerSegment(GameServerId gameServerId)
    {
        var value = gameServerId.ToString().Trim().ToLowerInvariant();

        if (value.Length is 0 or > 80
            || value is "." or ".."
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || value.StartsWith("\\\\", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(value)
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new StoragePlanningException(
                StorageIssueCodes.InvalidGameServerId,
                "Game server id cannot be used as a managed storage directory name.");
        }

        return value;
    }

    public static string EnsureContained(string root, string candidate, string message)
    {
        var fullRoot = NormalizeDirectoryPath(root);
        var fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && !fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoragePlanningException(StorageIssueCodes.InvalidGameServerId, message);
        }

        return fullCandidate;
    }

    public static string ResolveDataRoot(string configuredRoot)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "gameshud-data")
            : configuredRoot.Trim();

        return Path.GetFullPath(root);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        return $"{fullPath}{Path.DirectorySeparatorChar}";
    }
}
