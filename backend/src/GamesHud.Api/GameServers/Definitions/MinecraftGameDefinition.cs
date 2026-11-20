using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.GameServers.Definitions;

public static class LunchtimeArtifactManifest
{
    public const string PackId = "lunchtime-cozy-create-cooking";
    public const string PackVersion = "2.6.0-r2";
    public const string MinecraftVersion = "1.20.1";
    public const string ForgeVersion = "47.4.29";
    public const int JavaVersion = 17;
    public const int CurseForgeProjectId = 885927;
    public const int CurseForgeFileId = 9716699;
    public const string FileName = "Lunchtime_Cozy_Create_and_Cooking_Server_Pack_v2.6.0-r2.zip";
    public const string DownloadUrl = "https://mediafilez.forgecdn.net/files/9716/699/Lunchtime_Cozy_Create_and_Cooking_Server_Pack_v2.6.0-r2.zip";
    public const string Sha256 = "4478a08dbcb57708f7a2fb29f4db51ef44e65806e9b8b618a6ec770dd0458d9e";
    public const long ArchiveSizeBytes = 755_427_795;
    public const string ArchiveRoot = "Lunchtime_Cozy_Create_and_Cooking_Server_Pack_v2.6.0-r2";
}

public sealed class MinecraftGameDefinition :
    GameDefinition,
    IGameRequirementsDefinition,
    IGamePortDefinition,
    IGameStorageDefinition
{
    public const string RuntimeImageTag = "2026.11.0-java17";
    public const string RuntimeImageDigest = "sha256:633aab667752905d540e7723690ec0dcb19584492219ef074dd934b59e9d23a7";

    public MinecraftGameDefinition()
        : base(
            new GameId("minecraft"),
            "Minecraft Java",
            "Install the approved Lunchtime: Cozy Create Cooking server pack as a Managed Minecraft Java server.",
            new GameDefinitionBranding("minecraft"),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview, GameServerCapabilities.Logs, GameServerCapabilities.Mods],
            CreateRequirements(),
            [new GamePortDefinition(
                "game", "Game port", 25565, PortProtocols.Tcp, required: true,
                allowAlternative: true, exposure: PortExposures.Public,
                purpose: "Minecraft Java player traffic",
                source: "https://minecraft.wiki/w/Java_Edition_protocol/Packets")],
            [new GameStorageDefinition(
                "data", "Minecraft data", StoragePurposes.GameData, runtimeTarget: "/data",
                persistent: true, required: true, backupEligible: true, userData: true,
                minimumBytes: null,
                source: "https://github.com/itzg/docker-minecraft-server/blob/master/docs/index.md")],
            [new TrustedRuntimeImage(
                GameServerRuntime.DockerType,
                "ghcr.io",
                "itzg/minecraft-server",
                new ApprovedImageDigest(RuntimeImageDigest),
                new RuntimeImagePlatform("linux", "amd64"),
                $"itzg/minecraft-server:{RuntimeImageTag}")])
    {
    }

    public new GameRequirements Requirements => base.Requirements!;
    public new IReadOnlyCollection<GamePortDefinition> Ports => base.Ports;
    public new IReadOnlyCollection<GameStorageDefinition> Storages => base.Storages;

    private static GameRequirements CreateRequirements() => new(
        supportedOperatingSystems: ["linux"],
        supportedArchitectures: ["x64"],
        requiredRuntimes: [GameServerRuntime.DockerType],
        minimumLogicalProcessors: 2,
        recommendedLogicalProcessors: 4,
        memory: new ByteRequirement(
            minimumBytes: GameRequirementBytes.Gibibytes(7),
            recommendedBytes: GameRequirementBytes.Gibibytes(16)),
        storage: null,
        source: "https://github.com/Miso-Studio/Lunchtime-Cozy-Create-Farming",
        notes: "The approved pack requires Java 17 and documents 8 GB allocated RAM with 16 GB system RAM recommended. The container reserves additional non-heap overhead.");
}
