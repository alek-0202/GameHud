namespace GamesHud.Api.Palworld.Services;

public class PalworldConfigException : Exception
{
    public PalworldConfigException(string message)
        : base(message)
    {
    }

    public PalworldConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldConfigNotFoundException : PalworldConfigException
{
    public PalworldConfigNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldConfigValidationException : PalworldConfigException
{
    public PalworldConfigValidationException(IReadOnlyCollection<string> errors)
        : base("Palworld configuration values are invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyCollection<string> Errors { get; }
}

public sealed class PalworldConfigWriteException : PalworldConfigException
{
    public PalworldConfigWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldContainerLifecycleException : PalworldConfigException
{
    public PalworldContainerLifecycleException(string message)
        : base(message)
    {
    }
}
