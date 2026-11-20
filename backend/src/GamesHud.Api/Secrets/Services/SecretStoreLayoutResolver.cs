using GamesHud.Api.Persistence;

namespace GamesHud.Api.Secrets.Services;

public sealed class SecretStoreLayoutResolver : ISecretStoreLayoutResolver
{
    private readonly IPersistenceLayoutResolver _persistenceLayoutResolver;

    public SecretStoreLayoutResolver(IPersistenceLayoutResolver persistenceLayoutResolver)
    {
        _persistenceLayoutResolver = persistenceLayoutResolver;
    }

    public SecretStoreLayout ResolveLayout()
    {
        var persistenceLayout = _persistenceLayoutResolver.ResolveLayout();
        var secretsRoot = EnsureContained(
            persistenceLayout.DataRoot,
            Path.Combine(persistenceLayout.SystemRoot, "secrets"));

        return new SecretStoreLayout(
            persistenceLayout.DataRoot,
            persistenceLayout.SystemRoot,
            secretsRoot);
    }

    private static string EnsureContained(string dataRoot, string candidate)
    {
        var fullRoot = Path.GetFullPath(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Secret store path escaped the managed data root.");
        }

        return fullCandidate;
    }
}
