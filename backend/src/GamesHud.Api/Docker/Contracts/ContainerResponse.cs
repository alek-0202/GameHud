namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerResponse(
    string Id,
    string Name,
    string Image,
    string State,
    string Status);
