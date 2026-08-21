using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Services;

namespace GamesHud.Api.GameServers.Definitions;

public sealed class PalworldGameDefinition :
    GameDefinition,
    IGameRequirementsDefinition,
    IGamePortDefinition
{
    private const string RequirementsSource = "https://docs.palworldgame.com/getting-started/requirements/";
    private const string ServerArgumentsSource = "https://docs.palworldgame.com/settings-and-operation/arguments/";
    private const string ServerConfigurationSource = "https://docs.palworldgame.com/settings-and-operation/configuration/";
    private const string RestApiSource = "https://docs.palworldgame.com/api/rest-api/info/";

    public PalworldGameDefinition()
        : base(
            new GameId("palworld"),
            "Palworld",
            "Operate Palworld dedicated servers with supported management tools.",
            new GameDefinitionBranding("palworld"),
            [GameServerRuntime.DockerType],
            [
                GameServerCapabilities.Overview,
                GameServerCapabilities.Settings,
                GameServerCapabilities.Players,
                GameServerCapabilities.Backups,
                GameServerCapabilities.Update,
                GameServerCapabilities.Logs,
                GameServerCapabilities.PlayerManagement,
                GameServerCapabilities.Mods
            ],
            CreateRequirements(),
            CreatePorts())
    {
    }

    public new GameRequirements Requirements => base.Requirements!;

    public new IReadOnlyCollection<GamePortDefinition> Ports => base.Ports;

    private static GameRequirements CreateRequirements()
    {
        return new GameRequirements(
            supportedOperatingSystems: ["linux", "windows"],
            supportedArchitectures: ["x64"],
            requiredRuntimes: [GameServerRuntime.DockerType],
            minimumLogicalProcessors: null,
            recommendedLogicalProcessors: 4,
            memory: new ByteRequirement(
                minimumBytes: GameRequirementBytes.Gibibytes(8),
                recommendedBytes: GameRequirementBytes.Gibibytes(16)),
            storage: null,
            source: RequirementsSource,
            notes: "Pocketpair's server guide lists Windows/Linux 64-bit, 4 CPU cores recommended, 16 GB memory, and states 8 GB can boot with higher out-of-memory crash risk. It recommends faster SSD storage but does not publish a numeric storage minimum.");
    }

    private static IReadOnlyCollection<GamePortDefinition> CreatePorts()
    {
        return
        [
            new GamePortDefinition(
                "game",
                "Game port",
                8211,
                PortProtocols.Udp,
                required: true,
                allowAlternative: true,
                exposure: PortExposures.Public,
                purpose: "Player traffic",
                source: ServerArgumentsSource),
            new GamePortDefinition(
                "query",
                "Query port",
                27015,
                PortProtocols.Udp,
                required: false,
                allowAlternative: true,
                exposure: PortExposures.Public,
                purpose: "Community server discovery",
                source: ServerConfigurationSource),
            new GamePortDefinition(
                "rest-api",
                "REST API",
                8212,
                PortProtocols.Tcp,
                required: false,
                allowAlternative: true,
                exposure: PortExposures.Internal,
                purpose: "Private management API",
                source: RestApiSource)
        ];
    }
}
