namespace GamesHud.Api.Palworld.Updates.Services;

public class PalworldUpdateException : Exception
{
    public PalworldUpdateException(string message)
        : base(message)
    {
    }

    public PalworldUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldUpdateConfigurationException : PalworldUpdateException
{
    public PalworldUpdateConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldUpdateValidationException : PalworldUpdateException
{
    public PalworldUpdateValidationException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldUpdateCommandException : PalworldUpdateException
{
    public PalworldUpdateCommandException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldUpdateLifecycleException : PalworldUpdateException
{
    public PalworldUpdateLifecycleException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldUpdateFailedException : PalworldUpdateException
{
    public PalworldUpdateFailedException(
        string failedStep,
        string message,
        Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
        FailedStep = failedStep;
    }

    public string FailedStep { get; }
}
