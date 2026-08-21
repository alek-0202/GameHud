namespace GamesHud.Api.GameServers.Contracts;

public sealed record NetworkPortResponse(
    int Number,
    string Protocol);

public sealed record PortAvailabilityResponse(
    int Port,
    string Protocol,
    string Status,
    bool Available,
    bool DockerPublished,
    string Message);

public sealed record PortAllocationResponse(
    NetworkPortResponse RequestedPort,
    NetworkPortResponse? AllocatedPort,
    bool UsedAlternative,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyCollection<NetworkPortResponse> CheckedPorts);

public sealed record GamePortPlanResponse(
    string GameId,
    string DisplayName,
    string Status,
    IReadOnlyCollection<GamePortPlanItemResponse> Ports,
    string Message);

public sealed record GamePortPlanItemResponse(
    string DefinitionId,
    string Label,
    string Purpose,
    string Exposure,
    bool Required,
    bool AllowAlternative,
    PortAvailabilityResponse Availability,
    PortAllocationResponse Allocation);
