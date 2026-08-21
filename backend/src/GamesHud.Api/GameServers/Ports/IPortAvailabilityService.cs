namespace GamesHud.Api.GameServers.Ports;

public interface IPortAvailabilityService
{
    Task<PortAvailability> CheckAvailabilityAsync(
        NetworkPort port,
        CancellationToken cancellationToken);
}
