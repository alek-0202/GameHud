namespace GamesHud.Api.GameServers.Contracts;

public sealed record GameCatalogResponse(
    IReadOnlyCollection<GameCatalogItemResponse> Games);

public sealed record GameCatalogItemResponse(
    string Id,
    string DisplayName,
    string Description,
    GameCatalogBrandingResponse Branding,
    IReadOnlyCollection<string> SupportedRuntimes,
    IReadOnlyCollection<string> Capabilities);

public sealed record GameCatalogBrandingResponse(
    string IconKey,
    string? ImageReference);
