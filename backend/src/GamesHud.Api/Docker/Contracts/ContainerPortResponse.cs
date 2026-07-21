namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerPortResponse(
    int PrivatePort,
    int? PublicPort,
    string Type,
    string HostIp);
