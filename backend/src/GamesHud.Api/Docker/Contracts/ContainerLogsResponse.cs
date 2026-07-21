namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerLogsResponse(
    string ContainerId,
    IReadOnlyCollection<string> Lines,
    string RetrievedAt);
