namespace GamesHud.Api.Metrics.Services;

public class MetricsException : Exception
{
    public MetricsException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class HostMetricsUnavailableException : MetricsException
{
    public HostMetricsUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
