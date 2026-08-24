namespace GamesHud.Api.Secrets.Services;

public sealed class SecretStoreHealthService : ISecretStoreHealthService
{
    public const string ProviderName = "local";

    private readonly ISecretStoreLayoutResolver _layoutResolver;
    private readonly ISecretProtector _protector;

    public SecretStoreHealthService(
        ISecretStoreLayoutResolver layoutResolver,
        ISecretProtector protector)
    {
        _layoutResolver = layoutResolver;
        _protector = protector;
    }

    public SecretStoreHealthStatus GetStatus()
    {
        try
        {
            _layoutResolver.ResolveLayout();

            if (!_protector.IsAvailable(out _))
            {
                return new SecretStoreHealthStatus(
                    Available: false,
                    Provider: ProviderName,
                    Status: "unavailable",
                    ErrorCode: "secret_store_unavailable");
            }

            return new SecretStoreHealthStatus(
                Available: true,
                Provider: ProviderName,
                Status: "ready",
                ErrorCode: null);
        }
        catch (Exception)
        {
            return new SecretStoreHealthStatus(
                Available: false,
                Provider: ProviderName,
                Status: "unavailable",
                ErrorCode: "secret_store_unavailable");
        }
    }
}
