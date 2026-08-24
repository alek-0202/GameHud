using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.Secrets.Services;

public interface ISecretProtector
{
    bool IsAvailable(out string? errorCode);

    ProtectedSecretPayload Protect(SecretValue value);

    SecretValue Unprotect(ProtectedSecretPayload payload);
}
