namespace GamesHud.Api.Docker.Models;

public sealed class DockerUnavailableException : Exception
{
    public DockerUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
