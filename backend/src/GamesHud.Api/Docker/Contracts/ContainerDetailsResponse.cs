namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerDetailsResponse(
    string Id,
    string Name,
    string Image,
    string ImageId,
    string State,
    string Status,
    string CreatedAt,
    string StartedAt,
    string FinishedAt,
    long RestartCount,
    string Platform,
    string Driver,
    IReadOnlyCollection<ContainerPortResponse> Ports,
    IReadOnlyCollection<ContainerMountResponse> Mounts,
    IReadOnlyCollection<ContainerNetworkResponse> Networks,
    IReadOnlyDictionary<string, string> Labels);
