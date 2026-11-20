namespace GamesHud.Api.GameServers.Contracts;

public sealed record GameServerResponse(
    string Id,
    string GameType,
    string DisplayName,
    string RuntimeType,
    string InstallationType,
    string ContainerName,
    string? BrandingImage,
    IReadOnlyCollection<string> Capabilities);

public sealed record GameServersResponse(
    IReadOnlyCollection<GameServerResponse> Servers);
