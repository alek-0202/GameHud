using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Contracts;
using GamesHud.Api.Palworld.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldAdminServiceTests
{
    [Fact]
    public async Task BanUsesTheRequestedServerRestConfiguration()
    {
        var restService = new RecordingRestService();
        var service = new PalworldAdminService(CreateRegistry(), restService);

        await service.BanAsync(
            "solo",
            "steam-user",
            new PalworldPlayerActionRequest("BAN steam-user", "Rule violation"),
            CancellationToken.None);

        Assert.Equal("ban", restService.LastAction);
        Assert.Equal("http://solo:8212", restService.LastRestBaseUrl);
        Assert.Equal("steam-user", restService.LastUserId);
        Assert.Equal("Rule violation", restService.LastMessage);
    }

    [Fact]
    public async Task KickRequiresStrongConfirmation()
    {
        var restService = new RecordingRestService();
        var service = new PalworldAdminService(CreateRegistry(), restService);

        await Assert.ThrowsAsync<PalworldAdminValidationException>(() =>
            service.KickAsync(
                "amigos",
                "steam-user",
                new PalworldPlayerActionRequest("kick steam-user", null),
                CancellationToken.None));

        Assert.Null(restService.LastAction);
    }

    [Fact]
    public async Task AnnouncementRejectsHtmlLikeText()
    {
        var restService = new RecordingRestService();
        var service = new PalworldAdminService(CreateRegistry(), restService);

        await Assert.ThrowsAsync<PalworldAdminValidationException>(() =>
            service.AnnounceAsync(
                "amigos",
                new PalworldAnnouncementRequest("<b>hello</b>"),
                CancellationToken.None));

        Assert.Null(restService.LastAction);
    }

    [Fact]
    public async Task UnbanUsesExplicitUserIdAndConfirmation()
    {
        var restService = new RecordingRestService();
        var service = new PalworldAdminService(CreateRegistry(), restService);

        await service.UnbanAsync(
            "amigos",
            new PalworldUnbanRequest("steam-user", "UNBAN steam-user"),
            CancellationToken.None);

        Assert.Equal("unban", restService.LastAction);
        Assert.Equal("http://amigos:8212", restService.LastRestBaseUrl);
        Assert.Equal("steam-user", restService.LastUserId);
    }

    private static GameServerRegistry CreateRegistry()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Servers:0:Id"] = "amigos",
                ["Servers:0:Type"] = "palworld",
                ["Servers:0:ContainerName"] = "palworld-amigos",
                ["Servers:0:ManagedPath"] = "/srv/amigos",
                ["Servers:0:RestApi:BaseUrl"] = "http://amigos:8212",
                ["Servers:0:RestApi:Username"] = "admin-a",
                ["Servers:0:RestApi:Password"] = "secret-a",
                ["Servers:1:Id"] = "solo",
                ["Servers:1:Type"] = "palworld",
                ["Servers:1:ContainerName"] = "palworld-solo",
                ["Servers:1:ManagedPath"] = "/srv/solo",
                ["Servers:1:RestApi:BaseUrl"] = "http://solo:8212",
                ["Servers:1:RestApi:Username"] = "admin-b",
                ["Servers:1:RestApi:Password"] = "secret-b"
            })
            .Build();

        return new GameServerRegistry(
            configuration,
            Options.Create(new PalworldOptions()),
            [new PalworldGameServerPlugin()]);
    }

    private sealed class RecordingRestService : IPalworldRestService
    {
        public string? LastAction { get; private set; }

        public string? LastRestBaseUrl { get; private set; }

        public string? LastUserId { get; private set; }

        public string? LastMessage { get; private set; }

        public Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveWorldAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AnnounceAsync(string message, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AnnounceAsync(
            PalworldRestApiOptions restOptions,
            string message,
            CancellationToken cancellationToken)
        {
            Record("announce", restOptions, "server", message);
            return Task.CompletedTask;
        }

        public Task KickAsync(
            PalworldRestApiOptions restOptions,
            string userId,
            string? message,
            CancellationToken cancellationToken)
        {
            Record("kick", restOptions, userId, message);
            return Task.CompletedTask;
        }

        public Task BanAsync(
            PalworldRestApiOptions restOptions,
            string userId,
            string? message,
            CancellationToken cancellationToken)
        {
            Record("ban", restOptions, userId, message);
            return Task.CompletedTask;
        }

        public Task UnbanAsync(
            PalworldRestApiOptions restOptions,
            string userId,
            CancellationToken cancellationToken)
        {
            Record("unban", restOptions, userId, null);
            return Task.CompletedTask;
        }

        private void Record(
            string action,
            PalworldRestApiOptions restOptions,
            string userId,
            string? message)
        {
            LastAction = action;
            LastRestBaseUrl = restOptions.BaseUrl;
            LastUserId = userId;
            LastMessage = message;
        }
    }
}
