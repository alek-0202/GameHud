using GamesHud.Api.Docker.Contracts;

namespace GamesHud.Api.Docker.Services;

public interface IContainerService
{
    Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken);
}
