namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerNetworkResponse(
    string Name,
    string IpAddress,
    string Gateway,
    string MacAddress);
