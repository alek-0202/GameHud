using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Services;

namespace GamesHud.Api.Tests;

public sealed class GameDefinitionTests
{
    [Fact]
    public void GameDefinitionContainsOnlyStaticGameKnowledge()
    {
        var definition = CreateDefinition("valheim", "Valheim");

        Assert.Equal(new GameId("valheim"), definition.GameId);
        Assert.Equal("Valheim", definition.DisplayName);
        Assert.Equal("Dedicated server support for Valheim.", definition.Description);
        Assert.Equal("valheim", definition.Branding.IconKey);
        Assert.Equal([GameServerRuntime.DockerType], definition.SupportedRuntimes);
        Assert.Equal([GameServerCapabilities.Overview], definition.Capabilities);

        var propertyNames = typeof(GameDefinition)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => new[]
            {
                "ContainerId",
                "ContainerName",
                "ExternalReference",
                "ManagedPath",
                "Password",
                "AllocatedPorts",
                "RuntimeState"
            }.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PalworldDefinitionDeclaresImplementedMetadataAndCapabilities()
    {
        var definition = new PalworldGameDefinition();

        Assert.Equal(new GameId("palworld"), definition.GameId);
        Assert.Equal("Palworld", definition.DisplayName);
        Assert.Equal("palworld", definition.Branding.IconKey);
        Assert.Equal([GameServerRuntime.DockerType], definition.SupportedRuntimes);
        Assert.Equal(
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
            definition.Capabilities);
        Assert.DoesNotContain("provisioning", definition.Capabilities);
        Assert.DoesNotContain("one-click-install", definition.Capabilities);
    }

    [Fact]
    public void RegistryListsAndResolvesDefinitionsWithGameIdCaseBehavior()
    {
        var palworld = new PalworldGameDefinition();
        var valheim = CreateDefinition("valheim", "Valheim");
        var registry = new GameDefinitionRegistry([valheim, palworld]);

        Assert.Equal(2, registry.GetAll().Count);
        Assert.Same(palworld, registry.Get(new GameId("PALWORLD")));
        Assert.True(registry.TryGet(new GameId("Valheim"), out var resolved));
        Assert.Same(valheim, resolved);
    }

    [Fact]
    public void RegistryReturnsPredictableResultsForUnknownDefinitions()
    {
        var registry = new GameDefinitionRegistry([new PalworldGameDefinition()]);
        var unknownId = new GameId("unknown");

        Assert.False(registry.TryGet(unknownId, out var definition));
        Assert.Null(definition);
        Assert.Throws<GameDefinitionNotFoundException>(() => registry.Get(unknownId));
    }

    [Fact]
    public void RegistryRejectsDuplicateGameIds()
    {
        var exception = Assert.Throws<GameDefinitionConfigurationException>(() =>
            new GameDefinitionRegistry([
                CreateDefinition("palworld", "First"),
                CreateDefinition("PALWORLD", "Second")
            ]));

        Assert.Contains("palworld", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GameServerDoesNotRequireARegisteredDefinitionToExist()
    {
        var server = new GameServer(
            new GameServerId("future-server"),
            new GameId("future-game"),
            "Future Server",
            new GameServerRuntime(GameServerRuntime.DockerType, "future-container"),
            new GameServerInstallation(GameServerInstallationType.LegacyExternal));

        Assert.Equal(new GameId("future-game"), server.GameId);
    }

    private static GameDefinition CreateDefinition(string gameId, string displayName)
    {
        return new GameDefinition(
            new GameId(gameId),
            displayName,
            $"Dedicated server support for {displayName}.",
            new GameDefinitionBranding(gameId.ToLowerInvariant()),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview]);
    }
}
