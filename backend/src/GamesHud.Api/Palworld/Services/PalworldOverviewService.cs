using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Contracts;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldOverviewService : IPalworldOverviewService
{
    private const string DefaultServerName = "Palworld";

    private readonly IOptions<PalworldOptions> _options;
    private readonly IContainerService _containerService;
    private readonly IPalworldRestService _palworldRestService;
    private readonly ILogger<PalworldOverviewService> _logger;

    public PalworldOverviewService(
        IOptions<PalworldOptions> options,
        IContainerService containerService,
        IPalworldRestService palworldRestService,
        ILogger<PalworldOverviewService> logger)
    {
        _options = options;
        _containerService = containerService;
        _palworldRestService = palworldRestService;
        _logger = logger;
    }

    public async Task<PalworldOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var container = await _containerService.GetContainerDetailsAsync(containerName, cancellationToken);
        var retrievedAt = DateTimeOffset.UtcNow.ToString("O");

        if (container is null)
        {
            return CreateUnavailableOverview(
                containerName,
                "not-found",
                "Container not found",
                "Configured Palworld container was not found.",
                retrievedAt);
        }

        var containerState = container.State;

        if (IsStopped(containerState))
        {
            return CreateContainerOverview(
                container,
                "container-stopped",
                "Offline",
                "Palworld container is stopped.",
                retrievedAt);
        }

        if (IsStarting(containerState))
        {
            return CreateContainerOverview(
                container,
                "container-starting",
                "Starting",
                "Palworld container is starting.",
                retrievedAt);
        }

        try
        {
            var infoTask = _palworldRestService.GetInfoAsync(cancellationToken);
            var playersTask = _palworldRestService.GetPlayersAsync(cancellationToken);
            var settingsTask = _palworldRestService.GetSettingsAsync(cancellationToken);
            var metricsTask = _palworldRestService.GetMetricsAsync(cancellationToken);

            await Task.WhenAll(infoTask, playersTask, settingsTask, metricsTask);

            var info = await infoTask;
            var players = await playersTask;
            var settings = await settingsTask;
            var metrics = await metricsTask;
            var mappedPlayers = MapPlayers(players);
            var serverName = FirstNotBlank(info.ServerName, settings.ServerName) ?? DefaultServerName;

            return new PalworldOverviewResponse(
                serverName,
                serverName,
                container.Name,
                container.State,
                container.Status,
                "healthy",
                "Online",
                info.Version,
                FirstNotBlank(info.Description, settings.ServerDescription),
                ResolveConnectionAddress(),
                metrics.CurrentPlayerNum ?? mappedPlayers.Count,
                metrics.MaxPlayerNum ?? settings.ServerPlayerMaxNum,
                metrics.Uptime,
                metrics.ServerFps,
                metrics.ServerFrameTime,
                metrics.BaseCampNum,
                metrics.Days,
                true,
                null,
                mappedPlayers,
                retrievedAt);
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Palworld REST API is unavailable while building overview.");

            return CreateContainerOverview(
                container,
                "rest-unavailable",
                "REST unavailable",
                GetRestErrorMessage(exception),
                retrievedAt);
        }
    }

    public async Task<PalworldPlayersResponse> GetPlayersAsync(CancellationToken cancellationToken)
    {
        var playersTask = _palworldRestService.GetPlayersAsync(cancellationToken);
        var metricsTask = _palworldRestService.GetMetricsAsync(cancellationToken);
        var settingsTask = _palworldRestService.GetSettingsAsync(cancellationToken);

        await Task.WhenAll(playersTask, metricsTask, settingsTask);

        var players = MapPlayers(await playersTask);
        var metrics = await metricsTask;
        var settings = await settingsTask;

        return new PalworldPlayersResponse(
            metrics.CurrentPlayerNum ?? players.Count,
            metrics.MaxPlayerNum ?? settings.ServerPlayerMaxNum,
            players,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private PalworldOverviewResponse CreateContainerOverview(
        ContainerDetailsResponse container,
        string health,
        string healthLabel,
        string restApiMessage,
        string retrievedAt)
    {
        return new PalworldOverviewResponse(
            DefaultServerName,
            DefaultServerName,
            container.Name,
            container.State,
            container.Status,
            health,
            healthLabel,
            null,
            null,
            ResolveConnectionAddress(),
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            restApiMessage,
            [],
            retrievedAt);
    }

    private PalworldOverviewResponse CreateUnavailableOverview(
        string containerName,
        string health,
        string healthLabel,
        string message,
        string retrievedAt)
    {
        return new PalworldOverviewResponse(
            DefaultServerName,
            DefaultServerName,
            containerName,
            "unknown",
            "Unavailable",
            health,
            healthLabel,
            null,
            null,
            ResolveConnectionAddress(),
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            message,
            [],
            retrievedAt);
    }

    private string ResolveContainerName()
    {
        var containerName = _options.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new PalworldConfigException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }

    private string? ResolveConnectionAddress()
    {
        var connectionAddress = _options.Value.ConnectionAddress;

        return string.IsNullOrWhiteSpace(connectionAddress)
            ? null
            : connectionAddress.Trim();
    }

    private static IReadOnlyCollection<PalworldPlayerResponse> MapPlayers(PalworldRestPlayers players)
    {
        return players.Players
            .Select(player => new PalworldPlayerResponse(
                string.IsNullOrWhiteSpace(player.Name) ? "Unknown player" : player.Name.Trim(),
                string.IsNullOrWhiteSpace(player.AccountName) ? null : player.AccountName.Trim(),
                ToPublicId(player.PlayerId),
                player.Ping,
                player.Level))
            .ToArray();
    }

    private static string? ToPublicId(string? playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        var normalizedPlayerId = playerId.Trim();

        return normalizedPlayerId.Length <= 8
            ? normalizedPlayerId
            : normalizedPlayerId[^8..];
    }

    private static string? FirstNotBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool IsStopped(string state)
    {
        return state.Equals("created", StringComparison.OrdinalIgnoreCase)
            || state.Equals("exited", StringComparison.OrdinalIgnoreCase)
            || state.Equals("stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStarting(string state)
    {
        return state.Equals("restarting", StringComparison.OrdinalIgnoreCase)
            || state.Equals("starting", StringComparison.OrdinalIgnoreCase)
            || state.Equals("paused", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRestErrorMessage(PalworldRestException exception)
    {
        return exception switch
        {
            PalworldRestConfigurationException => "Palworld REST API is not configured in GamesHud.",
            PalworldRestUnauthorizedException => "Palworld REST API rejected the configured credentials.",
            PalworldRestMalformedResponseException => "Palworld REST API returned a malformed response.",
            _ => "Palworld REST API is unavailable."
        };
    }
}
