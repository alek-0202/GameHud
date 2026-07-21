using Docker.DotNet.Models;
using GamesHud.Api.Docker.Contracts;
using System.Globalization;

namespace GamesHud.Api.Docker.Services;

internal static class DockerContainerMapper
{
    private static readonly HashSet<string> AllowedLabelKeys = new(StringComparer.Ordinal)
    {
        "org.opencontainers.image.title",
        "org.opencontainers.image.description",
        "org.opencontainers.image.version",
        "org.opencontainers.image.vendor",
        "org.opencontainers.image.url",
        "org.opencontainers.image.source",
        "org.opencontainers.image.revision"
    };

    public static ContainerResponse Map(ContainerListResponse container)
    {
        return new ContainerResponse(
            container.ID ?? string.Empty,
            NormalizeName(container.Names),
            container.Image ?? string.Empty,
            container.State ?? string.Empty,
            container.Status ?? string.Empty);
    }

    public static ContainerDetailsResponse MapDetails(ContainerInspectResponse container)
    {
        var state = container.State?.Status ?? string.Empty;

        return new ContainerDetailsResponse(
            container.ID ?? string.Empty,
            NormalizeName(container.Name),
            container.Config?.Image ?? string.Empty,
            container.Image ?? string.Empty,
            state,
            state,
            FormatDateTime(container.Created),
            container.State?.StartedAt ?? string.Empty,
            container.State?.FinishedAt ?? string.Empty,
            container.RestartCount,
            container.Platform ?? string.Empty,
            container.Driver ?? string.Empty,
            MapPorts(container.NetworkSettings?.Ports),
            MapMounts(container.Mounts),
            MapNetworks(container.NetworkSettings?.Networks),
            FilterLabels(container.Config?.Labels));
    }

    private static string NormalizeName(IList<string>? names)
    {
        var name = names?.FirstOrDefault() ?? string.Empty;

        return name.TrimStart('/');
    }

    private static string NormalizeName(string? name)
    {
        return (name ?? string.Empty).TrimStart('/');
    }

    private static string FormatDateTime(DateTime value)
    {
        if (value == default)
        {
            return string.Empty;
        }

        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return utcValue.ToString("O", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyCollection<ContainerPortResponse> MapPorts(
        IDictionary<string, IList<PortBinding>>? ports)
    {
        if (ports is null || ports.Count == 0)
        {
            return Array.Empty<ContainerPortResponse>();
        }

        var mappedPorts = new List<ContainerPortResponse>();

        foreach (var port in ports)
        {
            var (privatePort, portType) = ParsePortKey(port.Key);

            if (port.Value is null || port.Value.Count == 0)
            {
                mappedPorts.Add(new ContainerPortResponse(
                    privatePort,
                    null,
                    portType,
                    string.Empty));

                continue;
            }

            foreach (var binding in port.Value)
            {
                mappedPorts.Add(new ContainerPortResponse(
                    privatePort,
                    ParsePublicPort(binding?.HostPort),
                    portType,
                    binding?.HostIP ?? string.Empty));
            }
        }

        return mappedPorts;
    }

    private static IReadOnlyCollection<ContainerMountResponse> MapMounts(
        IList<MountPoint>? mounts)
    {
        if (mounts is null || mounts.Count == 0)
        {
            return Array.Empty<ContainerMountResponse>();
        }

        return mounts
            .Select(mount => new ContainerMountResponse(
                mount.Type ?? string.Empty,
                mount.Source ?? string.Empty,
                mount.Destination ?? string.Empty,
                !mount.RW))
            .ToArray();
    }

    private static IReadOnlyCollection<ContainerNetworkResponse> MapNetworks(
        IDictionary<string, EndpointSettings>? networks)
    {
        if (networks is null || networks.Count == 0)
        {
            return Array.Empty<ContainerNetworkResponse>();
        }

        return networks
            .Select(network => new ContainerNetworkResponse(
                network.Key,
                network.Value?.IPAddress ?? string.Empty,
                network.Value?.Gateway ?? string.Empty,
                network.Value?.MacAddress ?? string.Empty))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> FilterLabels(
        IDictionary<string, string>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return labels
            .Where(label =>
                AllowedLabelKeys.Contains(label.Key)
                && !string.IsNullOrWhiteSpace(label.Value))
            .ToDictionary(
                label => label.Key,
                label => label.Value,
                StringComparer.Ordinal);
    }

    private static (int PrivatePort, string Type) ParsePortKey(string portKey)
    {
        var parts = portKey.Split('/', 2);
        var privatePort = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
            ? parsedPort
            : 0;
        var type = parts.Length > 1 ? parts[1] : string.Empty;

        return (privatePort, type);
    }

    private static int? ParsePublicPort(string? publicPort)
    {
        if (int.TryParse(publicPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
            return parsedPort;
        }

        return null;
    }
}
