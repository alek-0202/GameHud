using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.HostCapabilities.Services;

public interface IHostCapabilityService
{
    Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken);
}
