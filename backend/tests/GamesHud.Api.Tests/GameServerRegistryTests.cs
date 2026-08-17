using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
        Assert.Equal("palworld", server.ContainerName);
        Assert.Contains(GameServerCapabilities.Players, server.Capabilities);
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
            [new PalworldGameServerPlugin()]);
    }
}
