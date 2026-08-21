using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Services;

namespace GamesHud.Api.GameServers.Definitions;

public sealed class PalworldGameDefinition : GameDefinition, IGameRequirementsDefinition
{
    private const string RequirementsSource = "https://docs.palworldgame.com/getting-started/requirements/";

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
            CreateRequirements())
    {
    }

    public new GameRequirements Requirements => base.Requirements!;

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
}
