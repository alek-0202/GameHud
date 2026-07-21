namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerMountResponse(
    string Type,
    string Source,
    string Destination,
    bool ReadOnly);
