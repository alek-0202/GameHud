namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldOverviewResponse(
    string ServerName,
    string DisplayName,
    string ContainerName,
    string ContainerState,
    string ContainerStatus,
    string Health,
    string HealthLabel,
    string? Version,
    string? Description,
    string? ConnectionAddress,
    int OnlinePlayers,
    int? MaxPlayers,
    int? UptimeSeconds,
    int? ServerFps,
    double? ServerFrameTime,
    int? BaseCampCount,
    int? InGameDays,
    bool RestApiAvailable,
    string? RestApiMessage,
    IReadOnlyCollection<PalworldPlayerResponse> Players,
    string RetrievedAt);
