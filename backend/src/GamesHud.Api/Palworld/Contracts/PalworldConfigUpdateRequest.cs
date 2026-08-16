namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldConfigUpdateRequest(
    IReadOnlyCollection<PalworldSettingUpdateRequest> Settings);

public sealed record PalworldSettingUpdateRequest(
    string Key,
    string? Value);
