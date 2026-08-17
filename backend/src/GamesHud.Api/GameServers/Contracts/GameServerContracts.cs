namespace GamesHud.Api.GameServers.Contracts;

public sealed record GameServerResponse(
    string Id,
    string GameType,
    string DisplayName,
    string ContainerName,
    string? BrandingImage,
    IReadOnlyCollection<string> Capabilities);

public sealed record GameServersResponse(
    IReadOnlyCollection<GameServerResponse> Servers);
