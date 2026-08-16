namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldPlayersResponse(
    int OnlineCount,
    int? MaxPlayers,
    IReadOnlyCollection<PalworldPlayerResponse> Players,
    string RetrievedAt);

public sealed record PalworldPlayerResponse(
    string Name,
    string? AccountName,
    string? PublicId,
    double? Ping,
    int? Level);
