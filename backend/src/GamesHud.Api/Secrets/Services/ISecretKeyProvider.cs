namespace GamesHud.Api.Secrets.Services;

public interface ISecretKeyProvider
{
    bool IsAvailable(out string? errorCode);

    byte[] GetCurrentKey();
}
