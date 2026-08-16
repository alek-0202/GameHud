namespace GamesHud.Api.Palworld.Backups.Services;

public class PalworldBackupException : Exception
{
    public PalworldBackupException(string message)
        : base(message)
    {
    }

    public PalworldBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldBackupConfigurationException : PalworldBackupException
{
    public PalworldBackupConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldBackupNotFoundException : PalworldBackupException
{
    public PalworldBackupNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldBackupValidationException : PalworldBackupException
{
    public PalworldBackupValidationException(string message)
        : base(message)
    {
    }
}

public sealed class PalworldBackupWriteException : PalworldBackupException
{
    public PalworldBackupWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PalworldBackupRestoreException : PalworldBackupException
{
    public PalworldBackupRestoreException(string message, Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
    }
}

public sealed class PalworldBackupLifecycleException : PalworldBackupException
{
    public PalworldBackupLifecycleException(string message)
        : base(message)
    {
    }
}
