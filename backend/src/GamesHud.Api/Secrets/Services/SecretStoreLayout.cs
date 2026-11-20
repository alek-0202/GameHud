namespace GamesHud.Api.Secrets.Services;

public sealed record SecretStoreLayout(
    string DataRoot,
    string SystemRoot,
    string SecretsRoot);
