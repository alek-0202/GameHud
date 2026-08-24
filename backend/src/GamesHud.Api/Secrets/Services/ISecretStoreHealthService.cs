namespace GamesHud.Api.Secrets.Services;

public interface ISecretStoreHealthService
{
    SecretStoreHealthStatus GetStatus();
}
