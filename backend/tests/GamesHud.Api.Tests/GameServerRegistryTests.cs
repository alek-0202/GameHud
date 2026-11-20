using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Controllers;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Tests;

public sealed class GameServerRegistryTests
{
    [Fact]
    public void GetServersFallsBackToLegacyPalworldConfiguration()
    {
        var registry = CreateRegistry(
            [],
            new PalworldOptions
            {
                ContainerName = "palworld",
                ManagedPath = "/srv/palworld",
                RestApi = new PalworldRestApiOptions
                {
                    BaseUrl = "http://palworld:8212",
                    Username = "admin",
                    Password = "secret"
                }
            });

        var server = Assert.Single(registry.GetServers());

        Assert.Equal("palworld", server.Id);
        Assert.Equal("palworld", server.GameType);
        Assert.Equal(GameServerRuntime.DockerType, server.RuntimeType);
        Assert.Equal(GameServerInstallationType.LegacyExternal, server.InstallationType);
        Assert.Equal("palworld", server.ContainerName);
        Assert.Contains(GameServerCapabilities.Players, server.Capabilities);
    }

    [Fact]
    public void GetServersProjectsUnknownConfiguredGameAsExternalDockerRuntime()
    {
        var registry = CreateRegistry(new Dictionary<string, string?>
        {
            ["Servers:0:Id"] = "minecraft-lunchtime",
            ["Servers:0:Type"] = "minecraft",
            ["Servers:0:DisplayName"] = "Lunchtime",
            ["Servers:0:ContainerName"] = "minecraft-lunchtime"
        });

        var server = Assert.Single(registry.GetServers());

        Assert.Equal("minecraft-lunchtime", server.Id);
        Assert.Equal("minecraft", server.GameType);
        Assert.Equal(GameServerRuntime.DockerType, server.RuntimeType);
        Assert.Equal(GameServerInstallationType.LegacyExternal, server.InstallationType);
        Assert.Equal("minecraft-lunchtime", server.ContainerName);
        Assert.Empty(server.Capabilities);
    }

    [Fact]
    public void GetServerEndpointExposesNormalizedRuntimeAndInstallationOwnership()
    {
        var registry = CreateRegistry(new Dictionary<string, string?>
        {
            ["Servers:0:Id"] = "minecraft-lunchtime",
            ["Servers:0:Type"] = "minecraft",
            ["Servers:0:ContainerName"] = "minecraft-lunchtime"
        });
        var controller = new GameServersController(registry);

        var result = Assert.IsType<OkObjectResult>(controller.GetServer("minecraft-lunchtime"));
        var response = Assert.IsType<GameServerResponse>(result.Value);

        Assert.Equal("docker", response.RuntimeType);
        Assert.Equal("legacyExternal", response.InstallationType);
        Assert.Equal("minecraft-lunchtime", response.ContainerName);
    }

    [Fact]
    public void GetGameServersProjectsLegacyPalworldAsExternalDockerRuntime()
    {
        var registry = CreateRegistry(
            [],
            new PalworldOptions
            {
                ContainerName = "palworld-live",
                ManagedPath = "/private/palworld",
                BackupPath = "/private/backups",
                RestApi = new PalworldRestApiOptions
                {
                    BaseUrl = "http://private-rest:8212",
                    Username = "admin",
                    Password = "secret"
                }
            });

        var server = Assert.Single(registry.GetGameServers());

        Assert.Equal(new GameServerId("palworld"), server.Id);
        Assert.Equal(new GameId("palworld"), server.GameId);
        Assert.Equal(GameServerRuntime.DockerType, server.Runtime.Type);
        Assert.Equal("palworld-live", server.Runtime.ExternalReference);
        Assert.Equal(GameServerInstallationType.LegacyExternal, server.Installation.Type);
    }

    [Fact]
    public void GetGameServersProjectsEveryConfiguredServer()
    {
        var registry = CreateRegistry(new Dictionary<string, string?>
        {
            ["Servers:0:Id"] = "amigos",
            ["Servers:0:Type"] = "palworld",
            ["Servers:0:DisplayName"] = "Amigos",
            ["Servers:0:ContainerName"] = "palworld-amigos",
            ["Servers:1:Id"] = "solo",
            ["Servers:1:Type"] = "palworld",
            ["Servers:1:DisplayName"] = "Solo",
            ["Servers:1:ContainerName"] = "palworld-solo"
        });

        var servers = registry.GetGameServers();

        Assert.Equal(2, servers.Count);
        Assert.Contains(servers, server => server.Id == new GameServerId("amigos"));
        Assert.Contains(servers, server => server.Id == new GameServerId("solo"));
        Assert.All(servers, server =>
            Assert.Equal(GameServerInstallationType.LegacyExternal, server.Installation.Type));
    }

    [Fact]
    public void DuplicateServerIdsFailClearlyAndCaseInsensitively()
    {
        var registry = CreateRegistry(new Dictionary<string, string?>
        {
            ["Servers:0:Id"] = "amigos",
            ["Servers:0:Type"] = "palworld",
            ["Servers:0:ContainerName"] = "palworld-a",
            ["Servers:1:Id"] = "AMIGOS",
            ["Servers:1:Type"] = "palworld",
            ["Servers:1:ContainerName"] = "palworld-b"
        });

        var exception = Assert.Throws<GameServerConfigurationException>(() => registry.GetGameServers());

        Assert.Contains("amigos", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownDomainServerThrowsNotFoundException()
    {
        var registry = CreateRegistry([], new PalworldOptions { ContainerName = "palworld" });

        Assert.Throws<GameServerNotFoundException>(() =>
            registry.GetGameServer(new GameServerId("missing")));
    }

    [Fact]
    public void PublicServerContractRemainsLimitedToExistingFields()
    {
        var propertyNames = typeof(GameServerResponse)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["BrandingImage", "Capabilities", "ContainerName", "DisplayName", "GameType", "Id", "InstallationType", "RuntimeType"],
            propertyNames);
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetPalworldOptionsUsesTheRequestedServerConfiguration()
    {
        var registry = CreateRegistry(
            new Dictionary<string, string?>
            {
                ["Servers:0:Id"] = "amigos",
                ["Servers:0:Type"] = "palworld",
                ["Servers:0:DisplayName"] = "Amigos",
                ["Servers:0:ContainerName"] = "palworld-amigos",
                ["Servers:0:ManagedPath"] = "/srv/amigos",
                ["Servers:0:RestApi:BaseUrl"] = "http://amigos:8212",
                ["Servers:0:RestApi:Username"] = "admin-a",
                ["Servers:0:RestApi:Password"] = "secret-a",
                ["Servers:1:Id"] = "solo",
                ["Servers:1:Type"] = "palworld",
                ["Servers:1:DisplayName"] = "Solo",
                ["Servers:1:ContainerName"] = "palworld-solo",
                ["Servers:1:ManagedPath"] = "/srv/solo",
                ["Servers:1:RestApi:BaseUrl"] = "http://solo:8212",
                ["Servers:1:RestApi:Username"] = "admin-b",
                ["Servers:1:RestApi:Password"] = "secret-b"
            });

        var amigos = registry.GetPalworldOptions("amigos");
        var solo = registry.GetPalworldOptions("solo");

        Assert.Equal("palworld-amigos", amigos.ContainerName);
        Assert.Equal("/srv/amigos", amigos.ManagedPath);
        Assert.Equal("http://amigos:8212", amigos.RestApi.BaseUrl);
        Assert.Equal("palworld-solo", solo.ContainerName);
        Assert.Equal("/srv/solo", solo.ManagedPath);
        Assert.Equal("http://solo:8212", solo.RestApi.BaseUrl);
    }

    private static GameServerRegistry CreateRegistry(
        Dictionary<string, string?> values,
        PalworldOptions? legacyOptions = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new GameServerRegistry(
            configuration,
            Options.Create(legacyOptions ?? new PalworldOptions()),
            [new PalworldGameServerPlugin(new PalworldGameDefinition())]);
    }
}
