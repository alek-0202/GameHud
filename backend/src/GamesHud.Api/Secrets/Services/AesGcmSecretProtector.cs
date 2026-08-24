using System.Security.Cryptography;
using System.Text;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.Secrets.Services;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string AlgorithmName = "AES-256-GCM";
    private const int CurrentVersion = 1;
    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    private readonly ISecretKeyProvider _keyProvider;

    public AesGcmSecretProtector(ISecretKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public bool IsAvailable(out string? errorCode)
    {
        return _keyProvider.IsAvailable(out errorCode);
    }

    public ProtectedSecretPayload Protect(SecretValue value)
    {
        if (!IsAvailable(out var errorCode))
        {
            throw new SecretStoreUnavailableException(errorCode ?? "secret_store_unavailable");
        }

        var key = _keyProvider.GetCurrentKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var plainText = Encoding.UTF8.GetBytes(value.Reveal());
        var cipherText = new byte[plainText.Length];
        var tag = new byte[TagSizeInBytes];

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeInBytes);
            aesGcm.Encrypt(nonce, plainText, cipherText, tag);

            return new ProtectedSecretPayload(
                CurrentVersion,
                AlgorithmName,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(cipherText),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    public SecretValue Unprotect(ProtectedSecretPayload payload)
    {
        if (!IsAvailable(out var errorCode))
        {
            throw new SecretStoreUnavailableException(errorCode ?? "secret_store_unavailable");
        }

        if (payload.Version != CurrentVersion || payload.Algorithm != AlgorithmName)
        {
            throw new SecretStoreCorruptedException();
        }

        var key = _keyProvider.GetCurrentKey();
        var nonce = DecodeBase64(payload.Nonce);
        var cipherText = DecodeBase64(payload.Ciphertext);
        var tag = DecodeBase64(payload.Tag);
        var plainText = new byte[cipherText.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeInBytes);
            aesGcm.Decrypt(nonce, cipherText, tag, plainText);

            return SecretValue.FromPlainText(Encoding.UTF8.GetString(plainText));
        }
        catch (CryptographicException exception)
        {
            throw new SecretStoreCorruptedException(exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    private static byte[] DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new SecretStoreCorruptedException(exception);
        }
    }
}
