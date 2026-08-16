namespace GamesHud.Api.Palworld.Services;

public class PalworldRestException : Exception
{
    public PalworldRestException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldRestConfigurationException : PalworldRestException
{
    public PalworldRestConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldRestUnavailableException : PalworldRestException
{
    public PalworldRestUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldRestUnauthorizedException : PalworldRestException
{
    public PalworldRestUnauthorizedException()
        : base("Palworld REST API rejected the configured credentials.")
    {
    }
}

public sealed class PalworldRestMalformedResponseException : PalworldRestException
{
    public PalworldRestMalformedResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
