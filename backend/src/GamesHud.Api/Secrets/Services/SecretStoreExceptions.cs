namespace GamesHud.Api.Secrets.Services;

public class SecretStoreException : Exception
{
    public SecretStoreException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public SecretStoreException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class SecretStoreUnavailableException : SecretStoreException
{
    public SecretStoreUnavailableException(string errorCode)
        : base("Secret store is unavailable.", errorCode)
    {
    }

    public SecretStoreUnavailableException(string errorCode, Exception innerException)
        : base("Secret store is unavailable.", errorCode, innerException)
    {
    }
}

public sealed class SecretNotFoundException : SecretStoreException
{
    public SecretNotFoundException()
        : base("Secret was not found.", "secret_not_found")
    {
    }
}

public sealed class SecretStoreCorruptedException : SecretStoreException
{
    public SecretStoreCorruptedException()
        : base("Secret material could not be read.", "secret_unreadable")
    {
    }

    public SecretStoreCorruptedException(Exception innerException)
        : base("Secret material could not be read.", "secret_unreadable", innerException)
    {
    }
}
