namespace GamesHud.Api.Secrets.Contracts;

public sealed record SecretStoreStatusResponse(
    bool Available,
    string Provider,
    string Status,
    string? ErrorCode);
