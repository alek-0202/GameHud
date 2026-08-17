namespace GamesHud.Api.GameServers.Services;

public sealed record GameServerDescriptor(
    string Id,
    string GameType,
    string DisplayName,
    string ContainerName,
    string? BrandingImage,
    IReadOnlyCollection<string> Capabilities);
