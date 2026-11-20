using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Ports;

public static class PortProtocols
{
    public const string Tcp = "tcp";
    public const string Udp = "udp";

    public static bool IsSupported(string protocol)
    {
        return string.Equals(protocol, Tcp, StringComparison.Ordinal)
            || string.Equals(protocol, Udp, StringComparison.Ordinal);
    }
}

public static class PortExposures
{
    public const string Public = "public";
    public const string Internal = "internal";
}

public sealed record NetworkPort
{
    public NetworkPort(int number, string protocol)
    {
        if (number is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                "Port number must be between 1 and 65535.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        var normalizedProtocol = protocol.Trim().ToLowerInvariant();

        if (!PortProtocols.IsSupported(normalizedProtocol))
        {
            throw new ArgumentException("Unsupported port protocol.", nameof(protocol));
        }

        Number = number;
        Protocol = normalizedProtocol;
    }

    public int Number { get; }

    public string Protocol { get; }
}

public sealed record GamePortDefinition
{
    public GamePortDefinition(
        string id,
        string label,
        int defaultPort,
        string protocol,
        bool required,
        bool allowAlternative,
        string exposure,
        string purpose,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(exposure);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        Id = NormalizeIdentifier(id, nameof(id));
        Label = label.Trim();
        DefaultPort = new NetworkPort(defaultPort, protocol);
        Required = required;
        AllowAlternative = allowAlternative;
        Exposure = NormalizeExposure(exposure);
        Purpose = purpose.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    public string Id { get; }

    public string Label { get; }

    public NetworkPort DefaultPort { get; }

    public bool Required { get; }

    public bool AllowAlternative { get; }

    public string Exposure { get; }

    public string Purpose { get; }

    public string? Source { get; }

    private static string NormalizeIdentifier(string value, string parameterName)
    {
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new ArgumentException("Port definition id contains unsupported characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeExposure(string exposure)
    {
        var normalized = exposure.Trim().ToLowerInvariant();

        return normalized switch
        {
            PortExposures.Public => normalized,
            PortExposures.Internal => normalized,
            _ => throw new ArgumentException("Unsupported port exposure.", nameof(exposure))
        };
    }
}

public interface IGamePortDefinition
{
    IReadOnlyCollection<GamePortDefinition> Ports { get; }
}

public sealed record PortAllocationRequest(
    GameServerId GameServerId,
    string PortDefinitionId,
    NetworkPort PreferredPort,
    bool AllowAlternative,
    bool CoordinateInProcess = true);
