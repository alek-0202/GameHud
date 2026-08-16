namespace GamesHud.Api.Palworld.Services;

public sealed record PalworldRestInfo(
    string? Version,
    string? ServerName,
    string? Description,
    string? WorldGuid);

public sealed record PalworldRestPlayers(
    IReadOnlyCollection<PalworldRestPlayer> Players);

public sealed record PalworldRestPlayer(
    string? Name,
    string? AccountName,
    string? PlayerId,
    string? UserId,
    string? Ip,
    double? Ping,
    double? LocationX,
    double? LocationY,
    int? Level,
    int? BuildingCount);

public sealed record PalworldRestSettings(
    int? ServerPlayerMaxNum,
    string? ServerName,
    string? ServerDescription);

public sealed record PalworldRestMetrics(
    int? ServerFps,
    int? CurrentPlayerNum,
    double? ServerFrameTime,
    int? MaxPlayerNum,
    int? Uptime,
    int? BaseCampNum,
    int? Days);
