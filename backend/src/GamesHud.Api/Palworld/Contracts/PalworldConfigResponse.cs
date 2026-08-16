namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldConfigResponse(
    string ContainerName,
    IReadOnlyCollection<PalworldSettingResponse> Settings);

public sealed record PalworldSettingResponse(
    string Key,
    string Label,
    string Description,
    string Category,
    string Type,
    decimal? Min,
    decimal? Max,
    decimal? Step,
    IReadOnlyCollection<PalworldSettingOptionResponse> Options,
    string? DefaultValue,
    bool RestartRequired,
    bool Advanced,
    bool SecuritySensitive,
    string? Value,
    bool HasValue);

public sealed record PalworldSettingOptionResponse(
    string Value,
    string Label);
