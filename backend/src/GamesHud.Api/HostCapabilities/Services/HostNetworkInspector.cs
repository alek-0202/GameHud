using System.Net.NetworkInformation;
using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.HostCapabilities.Services;

public interface IHostNetworkInspector
{
    HostNetworkInfo GetNetworkInfo();
}

public interface IHostNetworkInfoProvider
{
    IReadOnlyCollection<NetworkInterfaceInfo> GetNetworkInterfaces();
}

public sealed record NetworkInterfaceInfo(
    bool IsUp,
    bool IsLoopback,
    bool SupportsIPv4);

public sealed class HostNetworkInspector : IHostNetworkInspector
{
    private readonly IHostNetworkInfoProvider _networkInfoProvider;

    public HostNetworkInspector(IHostNetworkInfoProvider networkInfoProvider)
    {
        _networkInfoProvider = networkInfoProvider;
    }

    public HostNetworkInfo GetNetworkInfo()
    {
        try
        {
            var interfaces = _networkInfoProvider.GetNetworkInterfaces();
            var usableInterfaces = interfaces.Where(networkInterface => networkInterface.IsUp).ToArray();
            var loopbackAvailable = interfaces.Any(networkInterface => networkInterface.IsLoopback);
            var ipv4Available = usableInterfaces.Any(networkInterface => networkInterface.SupportsIPv4);

            return new HostNetworkInfo(
                HostCapabilityStatuses.Available,
                usableInterfaces.Length,
                loopbackAvailable,
                ipv4Available,
                true);
        }
        catch (Exception)
        {
            return new HostNetworkInfo(
                HostCapabilityStatuses.Unavailable,
                0,
                false,
                false,
                false);
        }
    }
}

public sealed class RuntimeHostNetworkInfoProvider : IHostNetworkInfoProvider
{
    public IReadOnlyCollection<NetworkInterfaceInfo> GetNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(networkInterface => new NetworkInterfaceInfo(
                networkInterface.OperationalStatus == OperationalStatus.Up,
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                networkInterface.Supports(NetworkInterfaceComponent.IPv4)))
            .ToArray();
    }
}
