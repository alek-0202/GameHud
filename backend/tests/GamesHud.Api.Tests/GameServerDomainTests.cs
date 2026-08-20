using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.Tests;

public sealed class GameServerDomainTests
{
    [Fact]
    public void GameServerIdAcceptsAndTrimsAValidValue()
    {
        var id = new GameServerId("  amigos-e-amigos  ");

        Assert.Equal("amigos-e-amigos", id.Value);
        Assert.Equal("amigos-e-amigos", id.ToString());
        Assert.Equal(id, new GameServerId("AMIGOS-E-AMIGOS"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GameServerIdRejectsMissingValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new GameServerId(value!));
    }

    [Fact]
    public void GameIdAcceptsAndNormalizesAValidValue()
    {
        var id = new GameId("  Palworld  ");

        Assert.Equal("palworld", id.Value);
        Assert.Equal(id, new GameId("palworld"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GameIdRejectsMissingValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new GameId(value!));
    }

    [Fact]
    public void GameServerRepresentsInstanceRuntimeAndInstallationSeparately()
    {
        var server = new GameServer(
            new GameServerId("server-01"),
            new GameId("valheim"),
            "Friends Server",
            new GameServerRuntime(GameServerRuntime.DockerType, "valheim-container"),
            new GameServerInstallation(GameServerInstallationType.Managed));

        Assert.Equal("server-01", server.Id.Value);
        Assert.Equal("valheim", server.GameId.Value);
        Assert.Equal("Friends Server", server.DisplayName);
        Assert.Equal("docker", server.Runtime.Type);
        Assert.Equal("valheim-container", server.Runtime.ExternalReference);
        Assert.Equal(GameServerInstallationType.Managed, server.Installation.Type);
    }

    [Fact]
    public void GameServerRejectsDefaultIdentifiers()
    {
        var runtime = new GameServerRuntime(GameServerRuntime.DockerType, "container");
        var installation = new GameServerInstallation(GameServerInstallationType.Managed);

        Assert.Throws<ArgumentException>(() =>
            new GameServer(default, new GameId("palworld"), "Server", runtime, installation));
        Assert.Throws<ArgumentException>(() =>
            new GameServer(new GameServerId("server"), default, "Server", runtime, installation));
    }

    [Fact]
    public void RuntimeRejectsMissingValues()
    {
        Assert.ThrowsAny<ArgumentException>(() => new GameServerRuntime(null!, "container"));
        Assert.ThrowsAny<ArgumentException>(() => new GameServerRuntime(" ", "container"));
        Assert.Throws<ArgumentNullException>(() => new GameServerRuntime("docker", null!));
    }
}
