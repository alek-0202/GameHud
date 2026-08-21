namespace GamesHud.Api.GameServers.Ports;

public static class PortAvailabilityStatuses
{
    public const string Available = "available";
    public const string InUse = "in_use";
    public const string Unknown = "unknown";
}

public static class PortAllocationStatuses
{
    public const string Allocated = "allocated";
    public const string Failed = "failed";
}

public static class PortPlanStatuses
{
    public const string Ready = "ready";
    public const string ReadyWithAlternatives = "ready_with_alternatives";
    public const string Conflict = "conflict";
    public const string Unknown = "unknown";
}

public static class PortErrorCodes
{
    public const string InvalidPort = "invalid_port";
    public const string PortInUse = "port_in_use";
    public const string NoAlternativePort = "no_alternative_port";
    public const string UnsupportedProtocol = "unsupported_protocol";
    public const string UnknownGame = "unknown_game";
}

public sealed record PortAvailability(
    NetworkPort Port,
    string Status,
    bool IsAvailable,
    IReadOnlyCollection<DockerPublishedPort> DockerPublishedPorts,
    string Message);

public sealed record DockerPublishedPort(
    NetworkPort Port,
    string ContainerId,
    string ContainerName);

public sealed record PortAllocationResult(
    NetworkPort RequestedPort,
    NetworkPort? AllocatedPort,
    bool UsedAlternative,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyCollection<NetworkPort> CheckedPorts);

public sealed record GamePortPlan(
    string GameId,
    string DisplayName,
    string Status,
    IReadOnlyCollection<GamePortPlanItem> Ports,
    string Message);

public sealed record GamePortPlanItem(
    string DefinitionId,
    string Label,
    string Purpose,
    string Exposure,
    bool Required,
    bool AllowAlternative,
    PortAvailability Availability,
    PortAllocationResult Allocation);
