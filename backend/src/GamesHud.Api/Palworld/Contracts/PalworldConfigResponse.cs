namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldConfigResponse(
    string ContainerName,
    string? ServerName,
    bool HasServerPassword,
    decimal? ExpRate,
    decimal? PlayerDamageRateAttack,
    decimal? PalCaptureRate,
    decimal? PlayerStomachDecreaceRate,
    decimal? PlayerStaminaDecreaceRate,
    decimal? WorkSpeedRate,
    decimal? CollectionDropRate,
    decimal? EnemyDropItemRate,
    decimal? PalEggDefaultHatchingTime,
    string? DeathPenalty,
    int? GuildPlayerMaxNum,
    int? BaseCampMaxNum,
    int? BaseCampWorkerMaxNum);
