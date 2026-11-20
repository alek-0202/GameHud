using GamesHud.Api.GameServers.Storage;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Persistence;

public sealed class PersistenceLayoutResolver : IPersistenceLayoutResolver
{
    private const string DatabaseFileName = "gameshud.db";

    private readonly IOptions<StorageOptions> _storageOptions;

    public PersistenceLayoutResolver(IOptions<StorageOptions> storageOptions)
    {
        _storageOptions = storageOptions;
    }

    public PersistenceLayout ResolveLayout()
    {
        var dataRoot = ManagedStoragePathBuilder.ResolveDataRoot(_storageOptions.Value.DataRoot);
        var systemRoot = EnsureContained(
            dataRoot,
            Path.Combine(dataRoot, "system"),
            "Persistence system directory escaped the managed data root.");
        var databasePath = EnsureContained(
            dataRoot,
            Path.Combine(systemRoot, DatabaseFileName),
            "Persistence database path escaped the managed data root.");

        return new PersistenceLayout(dataRoot, systemRoot, databasePath);
    }

    private static string EnsureContained(string root, string candidate, string message)
    {
        var fullRoot = NormalizeDirectoryPath(root);
        var fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && !fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }

        return fullCandidate;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        return $"{fullPath}{Path.DirectorySeparatorChar}";
    }
}
