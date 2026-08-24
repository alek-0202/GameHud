namespace GamesHud.Api.Secrets.Services;

public sealed record ProtectedSecretPayload(
    int Version,
    string Algorithm,
    string Nonce,
    string Ciphertext,
    string Tag);
