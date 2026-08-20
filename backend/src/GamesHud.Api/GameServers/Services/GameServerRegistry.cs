using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Services;

public sealed class GameServerRegistry : IGameServerRegistry
{
    private const string LegacyPalworldServerId = "palworld";
    private const string ServersSectionName = "Servers";

    private readonly IConfiguration _configuration;
    private readonly IOptions<PalworldOptions> _legacyPalworldOptions;
    private readonly IReadOnlyDictionary<string, IGameServerPlugin> _plugins;

    public GameServerRegistry(
        IConfiguration configuration,
        IOptions<PalworldOptions> legacyPalworldOptions,
        IEnumerable<IGameServerPlugin> plugins)
    {
        _configuration = configuration;
        _legacyPalworldOptions = legacyPalworldOptions;
        _plugins = plugins.ToDictionary(
            plugin => plugin.GameType,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<GameServerDescriptor> GetServers()
    {
        return GetConfiguredServers()
            .Select(ToDescriptor)
            .ToArray();
    }

    public GameServerDescriptor GetServer(string serverId)
    {
        var server = GetConfiguredServers()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, serverId, StringComparison.OrdinalIgnoreCase));

        return server is null
            ? throw new GameServerNotFoundException(serverId)
            : ToDescriptor(server);
    }

    public IReadOnlyCollection<GameServer> GetGameServers()
    {
        return GetConfiguredServers()
            .Select(GameServerCompatibilityAdapter.ToDomain)
            .ToArray();
    }

    public GameServer GetGameServer(GameServerId serverId)
    {
        var server = GetConfiguredServers()
            .FirstOrDefault(candidate => new GameServerId(candidate.Id) == serverId);

        return server is null
            ? throw new GameServerNotFoundException(serverId.ToString())
            : GameServerCompatibilityAdapter.ToDomain(server);
    }

    public PalworldOptions GetPalworldOptions(string? serverId = null)
    {
        var servers = GetConfiguredServers();
        var server = string.IsNullOrWhiteSpace(serverId)
            ? servers.FirstOrDefault(candidate => IsPalworld(candidate.Type))
            : servers.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, serverId, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            throw new GameServerNotFoundException(serverId ?? LegacyPalworldServerId);
        }

        if (!IsPalworld(server.Type))
        {
            throw new GameServerNotFoundException(serverId ?? server.Id);
        }

        return ToPalworldOptions(server);
    }

    private IReadOnlyCollection<GameServerOptions> GetConfiguredServers()
    {
        var configuredServers = _configuration.GetSection(ServersSectionName).Get<List<GameServerOptions>>() ?? [];
        var validServers = configuredServers
            .Where(server => !string.IsNullOrWhiteSpace(server.Id)
                && !string.IsNullOrWhiteSpace(server.Type))
            .Select(Normalize)
            .ToList();

        if (validServers.Count == 0)
        {
            validServers.Add(CreateLegacyPalworldServer());
        }
        else if (!validServers.Any(server => IsPalworld(server.Type))
            && HasLegacyPalworldConfiguration())
        {
            validServers.Add(CreateLegacyPalworldServer());
        }

        var duplicateIds = validServers
            .GroupBy(server => server.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw new GameServerConfigurationException(
                $"Duplicate game server ids are not allowed: {string.Join(", ", duplicateIds)}.");
        }

        return validServers;
    }

    private GameServerDescriptor ToDescriptor(GameServerOptions server)
    {
        var capabilities = _plugins.TryGetValue(server.Type, out var plugin)
            ? plugin.Capabilities
            : Array.Empty<string>();

        return new GameServerDescriptor(
            server.Id,
            server.Type.Trim().ToLowerInvariant(),
            ResolveDisplayName(server),
            server.ContainerName.Trim(),
            string.IsNullOrWhiteSpace(server.BrandingImage) ? null : server.BrandingImage.Trim(),
            capabilities);
    }

    private PalworldOptions ToPalworldOptions(GameServerOptions server)
    {
        return new PalworldOptions
        {
            ManagedPath = server.ManagedPath,
            BackupPath = server.BackupPath,
            ContainerName = server.ContainerName,
            ConnectionAddress = server.ConnectionAddress,
            RestApi = server.RestApi,
            Backups = server.Backups
        };
    }

    private GameServerOptions CreateLegacyPalworldServer()
    {
        var legacy = _legacyPalworldOptions.Value;

        return new GameServerOptions
        {
            Id = LegacyPalworldServerId,
            Type = "palworld",
            DisplayName = string.IsNullOrWhiteSpace(legacy.ConnectionAddress)
                ? "Palworld"
                : "Palworld",
            ContainerName = legacy.ContainerName,
            ManagedPath = legacy.ManagedPath,
            BackupPath = legacy.BackupPath,
            ConnectionAddress = legacy.ConnectionAddress,
            RestApi = legacy.RestApi,
            Backups = legacy.Backups
        };
    }

    private bool HasLegacyPalworldConfiguration()
    {
        var legacy = _legacyPalworldOptions.Value;

        return !string.IsNullOrWhiteSpace(legacy.ContainerName)
            || !string.IsNullOrWhiteSpace(legacy.ManagedPath)
            || !string.IsNullOrWhiteSpace(legacy.RestApi.BaseUrl);
    }

    private static GameServerOptions Normalize(GameServerOptions server)
    {
        server.Id = server.Id.Trim();
        server.Type = server.Type.Trim().ToLowerInvariant();
        server.DisplayName = server.DisplayName.Trim();
        server.ContainerName = server.ContainerName.Trim();
        server.ManagedPath = server.ManagedPath.Trim();
        server.BackupPath = server.BackupPath.Trim();
        server.ConnectionAddress = server.ConnectionAddress.Trim();
        server.BrandingImage = server.BrandingImage.Trim();

        return server;
    }

    private static string ResolveDisplayName(GameServerOptions server)
    {
        if (!string.IsNullOrWhiteSpace(server.DisplayName))
        {
            return server.DisplayName.Trim();
        }

        return IsPalworld(server.Type) ? "Palworld" : server.Id;
    }

    private static bool IsPalworld(string type)
    {
        return type.Equals("palworld", StringComparison.OrdinalIgnoreCase);
    }
}
