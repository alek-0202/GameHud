namespace GamesHud.Api.Secrets.Services;

public sealed record SecretStoreHealthStatus(
    bool Available,
    string Provider,
    string Status,
    string? ErrorCode);
