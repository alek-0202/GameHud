using System.Globalization;
using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public enum PalworldSettingType
{
    Boolean,
    Integer,
    Decimal,
    String,
    Password,
    Select
}

public sealed record PalworldSettingOption(
    string Value,
    string Label);

public sealed record PalworldSettingDefinition(
    string Key,
    string Label,
    string Description,
    string Category,
    PalworldSettingType Type,
    decimal? Min,
    decimal? Max,
    decimal? Step,
    IReadOnlyCollection<PalworldSettingOption> Options,
    string? DefaultValue,
    bool RestartRequired,
    bool Advanced,
    bool SecuritySensitive)
{
    public PalworldSettingResponse ToResponse(string? value, bool hasValue)
    {
        return new PalworldSettingResponse(
            Key,
            Label,
            Description,
            Category,
            Type.ToString().ToLowerInvariant(),
            Min,
            Max,
            Step,
            Options
                .Select(option => new PalworldSettingOptionResponse(option.Value, option.Label))
                .ToArray(),
            DefaultValue,
            RestartRequired,
            Advanced,
            SecuritySensitive,
            Type is PalworldSettingType.Password ? null : value,
            hasValue);
    }
}

public static class PalworldSettingSchema
{
    private const bool RestartRequired = true;

    public static readonly IReadOnlyCollection<PalworldSettingDefinition> Settings =
    [
        Select("Difficulty", "Difficulty", "Overall game difficulty.", "General", "None", ["None", "Normal", "Difficult"]),
        Text("ServerName", "Server Name", "Public server name.", "General", "palworld-server-docker by Thijs van Loef"),
        Text("ServerDescription", "Server Description", "Public server description.", "General", "palworld-server-docker by Thijs van Loef"),
        Password("ServerPassword", "Server Password", "Password required to join the server.", "General"),
        Password("AdminPassword", "Admin Password", "Password used to obtain admin privileges.", "General"),
        Integer("ServerPlayerMaxNum", "Max Players", "Maximum number of players who can join.", "General", 1, 32, "16"),
        Text("Region", "Region", "Region label used by the server list.", "General", advanced: true),
        Text("PublicIP", "Public IP", "Explicit external IP for community server listing.", "General", advanced: true, security: true),
        Integer("PublicPort", "Public Port", "External port for community server listing.", "General", 1024, 65535, null, advanced: true),
        Boolean("bUseAuth", "Use Authentication", "Enable server authentication.", "General", "True", advanced: true, security: true),
        Text("BanListURL", "Ban List URL", "Ban list URL used by the server.", "General", "https://b.palworldgame.com/api/banlist.txt", advanced: true, security: true),

        Decimal("DayTimeSpeedRate", "Day Time Speed", "Daytime progression speed. Larger values mean shorter days.", "World", 0, 100, "1.000000"),
        Decimal("NightTimeSpeedRate", "Night Time Speed", "Nighttime progression speed. Larger values mean shorter nights.", "World", 0, 100, "1.000000"),
        Decimal("PalSpawnNumRate", "Pal Spawn Rate", "Pal appearance rate. Higher values can affect performance.", "World", 0, 100, "1.000000"),
        Integer("DropItemMaxNum", "Item Drop Max", "Maximum number of dropped items in the world.", "World", 0, 100000, "3000"),
        Integer("DropItemMaxNum_UNKO", "UNKO Drop Max", "Maximum number of UNKO drops in the world.", "World", 0, 100000, "100", advanced: true),
        Decimal("DropItemAliveMaxHours", "Drop Alive Time", "Hours before dropped items despawn.", "World", 0, 1000, "1.000000"),
        Decimal("AutoSaveSpan", "Auto Save Span", "Time between autosaves in seconds.", "World", 1, 3600, "30.000000"),
        Boolean("bEnableFastTravel", "Fast Travel", "Enable fast travel.", "World", "True"),
        Boolean("bEnableFastTravelOnlyBaseCamp", "Fast Travel Only Between Bases", "Restrict fast travel to bases only.", "World", "False", advanced: true),
        Boolean("bEnableInvaderEnemy", "Invaders", "Enable invader events.", "World", "True"),
        Boolean("bIsStartLocationSelectByMap", "Map Start Location", "Allow players to choose starting location on the map.", "World", "False"),
        Boolean("bExistPlayerAfterLogout", "Keep Player After Logout", "Keep players in the world after logout.", "World", "False", advanced: true),
        Boolean("bInvisibleOtherGuildBaseCampAreaFX", "Hide Other Guild Base Area FX", "Controls visibility of other guild base area effects.", "World", "False", advanced: true),
        Boolean("bBuildAreaLimit", "Build Area Limit", "Prevent building near restricted structures.", "World", "False"),
        Integer("SupplyDropSpan", "Supply Drop Span", "Meteorite and supply drop interval in minutes.", "World", 0, 10080, "180"),
        Select("RandomizerType", "Randomizer Type", "Pal spawn randomization mode.", "World", "None", ["None", "Region", "All"], advanced: true),
        Text("RandomizerSeed", "Randomizer Seed", "Seed used when randomization is enabled.", "World", "none", advanced: true),
        Boolean("bIsRandomizerPalLevelRandom", "Randomize Pal Levels", "Fully randomize wild Pal levels.", "World", "False", advanced: true),

        Decimal("ExpRate", "Experience Rate", "Experience gained by players.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerDamageRateAttack", "Player Damage Dealt", "Damage dealt by players.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerDamageRateDefense", "Player Damage Received", "Damage received by players.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerStomachDecreaceRate", "Player Hunger", "Player hunger depletion rate.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerStaminaDecreaceRate", "Player Stamina", "Player stamina depletion rate.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerAutoHPRegeneRate", "Player HP Regeneration", "Player natural HP regeneration rate.", "Player", 0, 100, "1.000000"),
        Decimal("PlayerAutoHpRegeneRateInSleep", "Player Sleep HP Regeneration", "Player HP regeneration while sleeping.", "Player", 0, 100, "1.000000"),
        Decimal("WorkSpeedRate", "Player Work Speed", "Player work speed multiplier.", "Player", 0, 100, "1.000000"),
        Decimal("ItemWeightRate", "Item Weight Rate", "Item weight multiplier.", "Player", 0, 100, "1.000000"),

        Decimal("PalCaptureRate", "Capture Rate", "Pal capture rate.", "Pals", 0, 100, "1.000000"),
        Decimal("PalDamageRateAttack", "Pal Damage Dealt", "Damage dealt by Pals.", "Pals", 0, 100, "1.000000"),
        Decimal("PalDamageRateDefense", "Pal Damage Received", "Damage received by Pals.", "Pals", 0, 100, "1.000000"),
        Decimal("PalStomachDecreaceRate", "Pal Hunger", "Pal hunger depletion rate.", "Pals", 0, 100, "1.000000"),
        Decimal("PalStaminaDecreaceRate", "Pal Stamina", "Pal stamina depletion rate.", "Pals", 0, 100, "1.000000"),
        Decimal("PalAutoHPRegeneRate", "Pal HP Regeneration", "Pal natural HP regeneration rate.", "Pals", 0, 100, "1.000000"),
        Decimal("PalAutoHpRegeneRateInSleep", "Pal Sleep HP Regeneration", "Pal HP regeneration while sleeping in Palbox.", "Pals", 0, 100, "1.000000"),
        Decimal("MonsterFarmActionSpeedRate", "Ranch Work Speed", "Item production speed from grazing.", "Pals", 0, 100, "1.000000"),

        Decimal("CollectionDropRate", "Collection Drop Rate", "Gatherable item multiplier.", "Resources & Drops", 0, 100, "1.000000"),
        Decimal("CollectionObjectHpRate", "Collection Object HP", "Gatherable object health multiplier.", "Resources & Drops", 0, 100, "1.000000"),
        Decimal("CollectionObjectRespawnSpeedRate", "Collection Respawn Interval", "Gatherable object respawn interval. Smaller values regenerate faster.", "Resources & Drops", 0, 100, "1.000000"),
        Decimal("EnemyDropItemRate", "Enemy Drop Rate", "Enemy dropped item multiplier.", "Resources & Drops", 0, 100, "1.000000"),
        Decimal("ItemCorruptionMultiplier", "Item Corruption Multiplier", "Item corruption speed multiplier.", "Resources & Drops", 0, 100, "1.000000", advanced: true),

        Decimal("BuildObjectHpRate", "Build Object HP", "Structure health multiplier.", "Building & Bases", 0, 100, "1.000000"),
        Decimal("BuildObjectDamageRate", "Build Object Damage", "Damage multiplier to buildings.", "Building & Bases", 0, 100, "1.000000"),
        Decimal("BuildObjectDeteriorationDamageRate", "Build Object Deterioration", "Building decay speed multiplier.", "Building & Bases", 0, 100, "1.000000"),
        Integer("BaseCampMaxNum", "Base Max", "Total number of bases across the server.", "Building & Bases", 0, 10000, "128"),
        Integer("BaseCampWorkerMaxNum", "Worker Max", "Maximum number of Pals per base. Official max is 50.", "Building & Bases", 1, 50, "15"),
        Integer("MaxBuildingLimitNum", "Building Limit", "Per-player building count cap. 0 means unlimited.", "Building & Bases", 0, 100000, "0"),
        Decimal("ServerReplicatePawnCullDistance", "Server Replicate Distance", "Pal sync distance from players in cm.", "Building & Bases", 5000, 15000, "15000.000000", advanced: true),
        Decimal("ServerReplicatePawnCullDistance_InBaseCamp", "Base Camp Replicate Distance", "Pal sync distance inside base camps in cm.", "Building & Bases", 0, 15000, "5000.000000", advanced: true),
        Boolean("bAllowEnemyCampSpawnNearBaseCamp", "Enemy Camps Near Bases", "Allow enemy camps to spawn near player bases.", "Building & Bases", "False", advanced: true),
        Boolean("bEnableBuildingPlayerUIdDisplay", "Show Building Owner ID", "Display building owner player ID.", "Building & Bases", "False", advanced: true),
        Integer("BuildingNameDisplayCacheTTLSeconds", "Building Name Cache TTL", "Building name display cache TTL in seconds.", "Building & Bases", 0, 3600, "60", advanced: true),

        Integer("GuildPlayerMaxNum", "Guild Player Max", "Maximum players per guild.", "Guild", 1, 1000, "20"),
        Integer("BaseCampMaxNumInGuild", "Guild Base Max", "Maximum bases per guild. Official max is 10.", "Guild", 1, 10, "4"),
        Boolean("bAutoResetGuildNoOnlinePlayers", "Auto Reset Empty Guild", "Automatically reset guild when no members are online.", "Guild", "False"),
        Decimal("AutoResetGuildTimeNoOnlinePlayers", "Empty Guild Reset Time", "Offline duration before empty guild reset triggers.", "Guild", 0, 10000, "72.000000"),
        Integer("GuildRejoinCooldownMinutes", "Guild Rejoin Cooldown", "Guild rejoin cooldown in minutes.", "Guild", 0, 10080, "0", advanced: true),
        Decimal("AutoTransferMasterCheckIntervalSeconds", "Master Transfer Check Interval", "Interval between guild master transfer checks.", "Guild", 0, 86400, "3600.000000", advanced: true),
        Integer("AutoTransferMasterThresholdDays", "Master Transfer Threshold", "Days of inactivity before guild master transfer.", "Guild", 0, 3650, "14", advanced: true),
        Integer("MaxGuildsPerFrame", "Max Guilds Per Frame", "Maximum guilds processed per frame.", "Guild", 1, 1000, "10", advanced: true),

        Decimal("PalEggDefaultHatchingTime", "Egg Incubation Time", "Hours to incubate a Huge Egg.", "Breeding", 0, 1000, "1.000000"),

        Select("DeathPenalty", "Death Penalty", "What players drop on death.", "Combat & PvP", "Item", ["None", "Item", "ItemAndEquipment", "All"]),
        Boolean("bIsPvP", "PvP", "Enable PvP.", "Combat & PvP", "False"),
        Boolean("bEnablePlayerToPlayerDamage", "Player-to-Player Damage", "Allow players to damage players.", "Combat & PvP", "False"),
        Boolean("bEnableFriendlyFire", "Friendly Fire", "Allow friendly fire.", "Combat & PvP", "False"),
        Boolean("bCanPickupOtherGuildDeathPenaltyDrop", "Pickup Other Guild Drops", "Allow other guilds to pick up death penalty drops.", "Combat & PvP", "False"),
        Boolean("bEnableDefenseOtherGuildPlayer", "Defend Against Other Guilds", "Allow defense against other guild players.", "Combat & PvP", "False"),
        Decimal("BlockRespawnTime", "Respawn Block Time", "Cooldown before respawn after death in seconds.", "Combat & PvP", 0, 10000, "5.000000", advanced: true),
        Decimal("RespawnPenaltyDurationThreshold", "Respawn Penalty Threshold", "Survival time threshold before respawn penalty applies.", "Combat & PvP", 0, 100000, "0.000000", advanced: true),
        Decimal("RespawnPenaltyTimeScale", "Respawn Penalty Scale", "Multiplier applied to respawn cooldown.", "Combat & PvP", 0, 100, "2.000000", advanced: true),
        Boolean("bDisplayPvPItemNumOnWorldMap_BaseCamp", "Show Base PvP Items On Map", "Show PvP item count in bases on world map.", "Combat & PvP", "False", advanced: true),
        Boolean("bDisplayPvPItemNumOnWorldMap_Player", "Show Player PvP Items On Map", "Show PvP item count for players on world map.", "Combat & PvP", "False", advanced: true),
        Text("AdditionalDropItemWhenPlayerKillingInPvPMode", "PvP Additional Drop Item", "Item ID dropped when PvP additional drops are enabled.", "Combat & PvP", "PlayerDropItem", advanced: true),
        Integer("AdditionalDropItemNumWhenPlayerKillingInPvPMode", "PvP Additional Drop Count", "Additional drop count when killing a player in PvP.", "Combat & PvP", 0, 10000, "1", advanced: true),
        Boolean("bAdditionalDropItemWhenPlayerKillingInPvPMode", "Enable PvP Additional Drops", "Enable special item drops when a player is killed in PvP.", "Combat & PvP", "False", advanced: true),

        Boolean("bIsMultiplay", "Multiplayer", "Enable multiplayer.", "Advanced", "False", advanced: true),
        Boolean("bHardcore", "Hardcore", "Enable hardcore mode.", "Advanced", "False", advanced: true),
        Boolean("bCharacterRecreateInHardcore", "Hardcore Character Recreation", "Allow character recreation after death in hardcore mode.", "Advanced", "False", advanced: true),
        Boolean("bPalLost", "Lose Pals On Death", "Permanently lose Pals on death.", "Advanced", "False", advanced: true),
        Boolean("bEnableNonLoginPenalty", "Non-login Penalty", "Enable non-login penalty.", "Advanced", "True", advanced: true),
        Boolean("bActiveUNKO", "Active UNKO", "Enable UNKO behavior.", "Advanced", "False", advanced: true),
        Boolean("bEnableAimAssistPad", "Controller Aim Assist", "Enable controller aim assist.", "Advanced", "True", advanced: true),
        Boolean("bEnableAimAssistKeyboard", "Keyboard Aim Assist", "Enable keyboard aim assist.", "Advanced", "False", advanced: true),
        Boolean("bShowPlayerList", "Show Player List", "Enable the player list on the ESC menu.", "Advanced", "False", advanced: true),
        Integer("ChatPostLimitPerMinute", "Chat Post Limit", "Messages players can send per minute.", "Advanced", 0, 10000, "30", advanced: true),
        Boolean("bEnablePredatorBossPal", "Predator Boss Pals", "Enable Predator boss Pals.", "Advanced", "True", advanced: true),
        Boolean("bIsUseBackupSaveData", "Use Backup Save Data", "Allow the server to load backup save data.", "Advanced", "True", advanced: true),
        Text("CrossplayPlatforms", "Crossplay Platforms", "Allowed platforms. Keep the parenthesized format.", "Advanced", "(Steam,Xbox,PS5,Mac)", advanced: true),
        Boolean("bAllowGlobalPalboxExport", "Global Palbox Export", "Allow saving to the global palbox.", "Advanced", "True", advanced: true),
        Boolean("bAllowGlobalPalboxImport", "Global Palbox Import", "Allow importing from the global palbox.", "Advanced", "False", advanced: true),
        Decimal("EquipmentDurabilityDamageRate", "Equipment Durability Damage", "Equipment durability loss multiplier.", "Advanced", 0, 100, "1.000000", advanced: true),
        Integer("PhysicsActiveDropItemMaxNum", "Physics Active Drop Max", "Maximum dropped items using physics. -1 uses game default.", "Advanced", -1, 100000, "-1", advanced: true),
        Boolean("bAllowClientMod", "Allow Client Mods", "Allow players with mods to join.", "Advanced", "True", advanced: true, security: true),
        Decimal("PlayerDataPalStorageUpdateCheckTickInterval", "Pal Storage Update Tick", "Player data Pal storage update check interval.", "Advanced", 0, 10000, "1.000000", advanced: true),
        Select("LogFormatType", "Log Format", "Palworld log format.", "Advanced", "Text", ["Text", "Json"], advanced: true),
        Boolean("bIsShowJoinLeftMessage", "Join/Leave Messages", "Show in-game join and leave messages.", "Advanced", "True", advanced: true),
        Text("DenyTechnologyList", "Deny Technology List", "Technology IDs to disable.", "Advanced", advanced: true),
        Boolean("bEnableVoiceChat", "Voice Chat", "Enable in-game voice chat.", "Advanced", "False", advanced: true),
        Decimal("VoiceChatMaxVolumeDistance", "Voice Chat Max Distance", "Distance where voice chat volume stops attenuating.", "Advanced", 0, 100000, "3000.000000", advanced: true),
        Decimal("VoiceChatZeroVolumeDistance", "Voice Chat Zero Distance", "Distance where voice chat becomes silent.", "Advanced", 0, 100000, "15000.000000", advanced: true),
        Boolean("bAllowEnhanceStat_Health", "Enhance Health Stat", "Allow stat point enhancement for health.", "Advanced", "True", advanced: true),
        Boolean("bAllowEnhanceStat_Attack", "Enhance Attack Stat", "Allow stat point enhancement for attack.", "Advanced", "True", advanced: true),
        Boolean("bAllowEnhanceStat_Stamina", "Enhance Stamina Stat", "Allow stat point enhancement for stamina.", "Advanced", "True", advanced: true),
        Boolean("bAllowEnhanceStat_Weight", "Enhance Weight Stat", "Allow stat point enhancement for carry weight.", "Advanced", "True", advanced: true),
        Boolean("bAllowEnhanceStat_WorkSpeed", "Enhance Work Speed Stat", "Allow stat point enhancement for work speed.", "Advanced", "True", advanced: true),
        Boolean("RCONEnabled", "RCON Enabled", "Enable RCON.", "Advanced", "False", advanced: true, security: true),
        Integer("RCONPort", "RCON Port", "RCON listening port.", "Advanced", 1024, 65535, "25575", advanced: true, security: true),
        Boolean("RESTAPIEnabled", "REST API Enabled", "Enable Palworld REST API.", "Advanced", "True", advanced: true, security: true),
        Integer("RESTAPIPort", "REST API Port", "Palworld REST API listening port.", "Advanced", 1024, 65535, "8212", advanced: true, security: true)
    ];

    public static readonly IReadOnlyDictionary<string, PalworldSettingDefinition> ByKey =
        Settings.ToDictionary(setting => setting.Key, StringComparer.Ordinal);

    public static string FormatDecimal(decimal value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static PalworldSettingDefinition Boolean(
        string key,
        string label,
        string description,
        string category,
        string defaultValue,
        bool advanced = false,
        bool security = false)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.Boolean,
            null,
            null,
            null,
            [],
            defaultValue,
            RestartRequired,
            advanced,
            security);
    }

    private static PalworldSettingDefinition Integer(
        string key,
        string label,
        string description,
        string category,
        decimal? min,
        decimal? max,
        string? defaultValue,
        bool advanced = false,
        bool security = false)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.Integer,
            min,
            max,
            1,
            [],
            defaultValue,
            RestartRequired,
            advanced,
            security);
    }

    private static PalworldSettingDefinition Decimal(
        string key,
        string label,
        string description,
        string category,
        decimal? min,
        decimal? max,
        string defaultValue,
        bool advanced = false)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.Decimal,
            min,
            max,
            0.1m,
            [],
            defaultValue,
            RestartRequired,
            advanced,
            false);
    }

    private static PalworldSettingDefinition Text(
        string key,
        string label,
        string description,
        string category,
        string? defaultValue = null,
        bool advanced = false,
        bool security = false)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.String,
            null,
            null,
            null,
            [],
            defaultValue,
            RestartRequired,
            advanced,
            security);
    }

    private static PalworldSettingDefinition Password(
        string key,
        string label,
        string description,
        string category)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.Password,
            null,
            null,
            null,
            [],
            null,
            RestartRequired,
            false,
            true);
    }

    private static PalworldSettingDefinition Select(
        string key,
        string label,
        string description,
        string category,
        string defaultValue,
        string[] options,
        bool advanced = false)
    {
        return new PalworldSettingDefinition(
            key,
            label,
            description,
            category,
            PalworldSettingType.Select,
            null,
            null,
            null,
            options.Select(option => new PalworldSettingOption(option, option)).ToArray(),
            defaultValue,
            RestartRequired,
            advanced,
            false);
    }
}
