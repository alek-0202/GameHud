namespace GamesHud.Api.GameServers.Ports;

public interface IDockerPublishedPortProvider
{
    Task<IReadOnlyCollection<DockerPublishedPort>> GetPublishedPortsAsync(
        CancellationToken cancellationToken);
}
