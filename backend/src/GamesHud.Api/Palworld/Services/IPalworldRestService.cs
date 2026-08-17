using GamesHud.Api.Palworld.Configuration;

namespace GamesHud.Api.Palworld.Services;

public interface IPalworldRestService
{
    Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken);

    Task<PalworldRestInfo> GetInfoAsync(
        PalworldRestApiOptions restOptions,
        CancellationToken cancellationToken)
    {
        return GetInfoAsync(cancellationToken);
    }

    Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken);

    Task<PalworldRestPlayers> GetPlayersAsync(
        PalworldRestApiOptions restOptions,
        CancellationToken cancellationToken)
    {
        return GetPlayersAsync(cancellationToken);
    }

    Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken);

    Task<PalworldRestSettings> GetSettingsAsync(
        PalworldRestApiOptions restOptions,
        CancellationToken cancellationToken)
    {
        return GetSettingsAsync(cancellationToken);
    }

    Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken);

    Task<PalworldRestMetrics> GetMetricsAsync(
        PalworldRestApiOptions restOptions,
        CancellationToken cancellationToken)
    {
        return GetMetricsAsync(cancellationToken);
    }

    Task SaveWorldAsync(CancellationToken cancellationToken);

    Task SaveWorldAsync(
        PalworldRestApiOptions restOptions,
        CancellationToken cancellationToken)
    {
        return SaveWorldAsync(cancellationToken);
    }

    Task AnnounceAsync(
        string message,
        CancellationToken cancellationToken);

    Task AnnounceAsync(
        PalworldRestApiOptions restOptions,
        string message,
        CancellationToken cancellationToken)
    {
        return AnnounceAsync(message, cancellationToken);
    }

    Task KickAsync(
        PalworldRestApiOptions restOptions,
        string userId,
        string? message,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Palworld kick is not implemented by this REST service.");
    }

    Task BanAsync(
        PalworldRestApiOptions restOptions,
        string userId,
        string? message,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Palworld ban is not implemented by this REST service.");
    }

    Task UnbanAsync(
        PalworldRestApiOptions restOptions,
        string userId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Palworld unban is not implemented by this REST service.");
    }
}
