namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldConfigUpdateResponse(
    string Message,
    string ContainerName,
    bool RestartRequested,
    bool LifecycleApplied,
    string BackupFileName,
    PalworldConfigResponse Config);
