using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Services;

public sealed record GameServerDescriptor(
    string Id,
    string GameType,
    string DisplayName,
    string RuntimeType,
    GameServerInstallationType InstallationType,
    string ContainerName,
    string? BrandingImage,
    IReadOnlyCollection<string> Capabilities);
