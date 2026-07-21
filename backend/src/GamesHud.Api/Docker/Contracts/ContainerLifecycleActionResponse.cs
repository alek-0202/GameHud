namespace GamesHud.Api.Docker.Contracts;

public sealed record ContainerLifecycleActionResponse(
    string ContainerId,
    string Action,
    bool Success,
    string Message,
    string PreviousState,
    string CurrentState,
    string CompletedAt);
