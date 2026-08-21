using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GamesHud.Api.GameServers.Ports;

public sealed class PortAvailabilityService : IPortAvailabilityService
{
    private readonly IDockerPublishedPortProvider _dockerPublishedPortProvider;

    public PortAvailabilityService(IDockerPublishedPortProvider dockerPublishedPortProvider)
    {
        _dockerPublishedPortProvider = dockerPublishedPortProvider;
    }

    public async Task<PortAvailability> CheckAvailabilityAsync(
        NetworkPort port,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(port);

        var dockerPorts = await _dockerPublishedPortProvider.GetPublishedPortsAsync(cancellationToken);
        var matchingDockerPorts = dockerPorts
            .Where(candidate => candidate.Port == port)
            .ToArray();
        var isAvailable = port.Protocol switch
        {
            PortProtocols.Tcp => IsTcpAvailable(port.Number),
            PortProtocols.Udp => IsUdpAvailable(port.Number),
            _ => throw new ArgumentException("Unsupported port protocol.", nameof(port))
        };

        if (isAvailable)
        {
            return new PortAvailability(
                port,
                PortAvailabilityStatuses.Available,
                true,
                matchingDockerPorts,
                "Port appears available on this host.");
        }

        return new PortAvailability(
            port,
            PortAvailabilityStatuses.InUse,
            false,
            matchingDockerPorts,
            "Port is already in use on this host.");
    }

    private static bool IsTcpAvailable(int port)
    {
        if (IPGlobalProperties
            .GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port))
        {
            return false;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsUdpAvailable(int port)
    {
        if (IPGlobalProperties
            .GetIPGlobalProperties()
            .GetActiveUdpListeners()
            .Any(endpoint => endpoint.Port == port))
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true
            };
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
