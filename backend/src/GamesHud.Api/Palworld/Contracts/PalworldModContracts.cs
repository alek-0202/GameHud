namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldModsResponse(
    string ServerId,
    bool ManagementSupported,
    string Message,
    IReadOnlyCollection<PalworldDetectedModResponse> DetectedMods);

public sealed record PalworldDetectedModResponse(
    string Name,
    string RelativePath,
    long SizeBytes);
