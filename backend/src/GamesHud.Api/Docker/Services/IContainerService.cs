using GamesHud.Api.Docker.Contracts;

namespace GamesHud.Api.Docker.Services;

public interface IContainerService
{
    Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken);

    Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
        string containerId,
        CancellationToken cancellationToken);

    Task<ContainerLogsResponse?> GetContainerLogsAsync(
        string containerId,
        int tail,
        bool timestamps,
        CancellationToken cancellationToken);

    Task<ContainerLifecycleActionResponse?> StartContainerAsync(
        string containerId,
        CancellationToken cancellationToken);

    Task<ContainerLifecycleActionResponse?> StopContainerAsync(
        string containerId,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
        string containerId,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
