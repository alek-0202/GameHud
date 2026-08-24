using System.Security.Cryptography;
using GamesHud.Api.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Secrets.Services;

public sealed class ConfigurationSecretKeyProvider : ISecretKeyProvider
{
    private const int KeySizeInBytes = 32;

    private readonly IOptions<SecretStorageOptions> _options;

    public ConfigurationSecretKeyProvider(IOptions<SecretStorageOptions> options)
    {
        _options = options;
    }

    public bool IsAvailable(out string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.MasterKey))
        {
            errorCode = "secret_master_key_missing";
            return false;
        }

        if (!TryDecode(_options.Value.MasterKey, out var key) || key.Length != KeySizeInBytes)
        {
            errorCode = "secret_master_key_invalid";
            if (key.Length > 0)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            return false;
        }

        CryptographicOperations.ZeroMemory(key);
        errorCode = null;
        return true;
    }

    public byte[] GetCurrentKey()
    {
        if (string.IsNullOrWhiteSpace(_options.Value.MasterKey))
        {
            throw new SecretStoreUnavailableException("secret_master_key_missing");
        }

        if (!TryDecode(_options.Value.MasterKey, out var key) || key.Length != KeySizeInBytes)
        {
            if (key.Length > 0)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            throw new SecretStoreUnavailableException("secret_master_key_invalid");
        }

        return key;
    }

    private static bool TryDecode(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
