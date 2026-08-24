using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.Secrets.Services;

public interface ISecretStore
{
    Task<SecretReference> StoreAsync(
        SecretPurpose purpose,
        SecretValue value,
        CancellationToken cancellationToken = default);

    Task<SecretValue> GetAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        SecretReference reference,
        SecretValue value,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default);
}
