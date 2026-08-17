using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldModsService : IPalworldModsService
{
    private static readonly string[] CandidateDirectories =
    [
        "Pal/Content/Paks",
        "Pal/Content/Mods",
        "Pal/Binaries/Linux/Mods",
        "Pal/Saved/Mods"
    ];

    private static readonly HashSet<string> KnownModExtensions = new(
        [".pak", ".ucas", ".utoc", ".dll"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IGameServerRegistry _gameServerRegistry;

    public PalworldModsService(IGameServerRegistry gameServerRegistry)
    {
        _gameServerRegistry = gameServerRegistry;
    }

    public Task<PalworldModsResponse> GetModsAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        var options = _gameServerRegistry.GetPalworldOptions(serverId);
        var managedPath = ResolveManagedPath(options.ManagedPath);
        var detectedMods = new List<PalworldDetectedModResponse>();

        foreach (var relativeDirectory in CandidateDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateDirectory = Path.GetFullPath(
                Path.Combine(new[] { managedPath }.Concat(relativeDirectory.Split('/')).ToArray()));

            if (!IsInsideManagedPath(managedPath, candidateDirectory)
                || !Directory.Exists(candidateDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(candidateDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!KnownModExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(file);

                if (!IsInsideManagedPath(managedPath, fullPath))
                {
                    continue;
                }

                var info = new FileInfo(fullPath);
                detectedMods.Add(new PalworldDetectedModResponse(
                    Path.GetFileName(fullPath),
                    Path.GetRelativePath(managedPath, fullPath),
                    info.Length));
            }
        }

        return Task.FromResult(new PalworldModsResponse(
            serverId,
            ManagementSupported: false,
            "Mod installation and enable/disable are not automated because the current Linux container setup does not provide a safe, uniform Palworld mod management mechanism.",
            detectedMods
                .OrderBy(mod => mod.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray()));
    }

    private static string ResolveManagedPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new PalworldConfigException("Palworld managed path is not configured.");
        }

        var fullPath = Path.GetFullPath(configuredPath);

        if (!Directory.Exists(fullPath))
        {
            throw new PalworldConfigNotFoundException("Configured Palworld managed path was not found.");
        }

        return fullPath;
    }

    private static bool IsInsideManagedPath(string managedPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedPath));
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }
}
