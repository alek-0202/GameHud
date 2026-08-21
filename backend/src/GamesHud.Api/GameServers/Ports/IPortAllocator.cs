namespace GamesHud.Api.GameServers.Ports;

public interface IPortAllocator
{
    Task<PortAllocationResult> AllocateAsync(
        PortAllocationRequest request,
        CancellationToken cancellationToken);
}
