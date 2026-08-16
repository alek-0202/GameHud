using System.Net;
using System.Net.Http.Json;
using System.Text;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Contracts;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldRestTests
{
    [Fact]
    public async Task RestServiceParsesPlayers()
    {
        var handler = new StaticJsonHandler("""
            {
              "players": [
                {
                  "name": "Alek",
                  "accountName": "alek-account",
                  "playerId": "AFAFD830000000000000000000000000",
                  "userId": "steam_00000000000000000",
                  "ip": "127.0.0.1",
                  "ping": 12.5,
                  "location_x": 123.45,
                  "location_y": 67.89,
                  "level": 31,
                  "building_count": 42
                }
              ]
            }
            """);
        var service = CreateRestService(handler);

        var result = await service.GetPlayersAsync(CancellationToken.None);

        var player = Assert.Single(result.Players);
        Assert.Equal("Alek", player.Name);
        Assert.Equal("alek-account", player.AccountName);
        Assert.Equal(12.5, player.Ping);
        Assert.Equal(31, player.Level);
        Assert.Equal("127.0.0.1", player.Ip);
        Assert.Equal("Basic", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task RestServicePostsAnnounceEndpoint()
    {
        var handler = new StaticJsonHandler("{}");
        var service = CreateRestService(handler);

        await service.AnnounceAsync("Maintenance soon.", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("http://palworld.local:8212/v1/api/announce", handler.LastUri?.ToString());
        Assert.Equal("Basic", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task OverviewPlayersCanBeEmpty()
    {
        var service = CreateOverviewService(
            new FakePalworldRestService
            {
                Players = new PalworldRestPlayers([]),
                Metrics = new PalworldRestMetrics(60, 0, 16.1, 32, 300, 3, 12),
                Settings = new PalworldRestSettings(32, "Amigos e Amigos", "Private world")
            });

        var result = await service.GetPlayersAsync(CancellationToken.None);

        Assert.Equal(0, result.OnlineCount);
        Assert.Equal(32, result.MaxPlayers);
        Assert.Empty(result.Players);
    }

    [Fact]
    public async Task RestUnavailableThrowsFriendlyException()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        var service = CreateRestService(handler);

        await Assert.ThrowsAsync<PalworldRestUnavailableException>(
            () => service.GetInfoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestTimeoutThrowsUnavailableException()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(5));
        var service = CreateRestService(handler, timeoutSeconds: 1);

        await Assert.ThrowsAsync<PalworldRestUnavailableException>(
            () => service.GetInfoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MalformedResponseThrowsFriendlyException()
    {
        var handler = new StaticJsonHandler("{");
        var service = CreateRestService(handler);

        await Assert.ThrowsAsync<PalworldRestMalformedResponseException>(
            () => service.GetMetricsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OverviewCombinesDockerAndPalworldRestData()
    {
        var restService = new FakePalworldRestService
        {
            Info = new PalworldRestInfo("v1.0.3", "Amigos e Amigos", "Private world", "world-guid"),
            Players = new PalworldRestPlayers(
            [
                new PalworldRestPlayer("Alek", "alek-account", "AFAFD830000000000000000000000000", "steam_private", "10.0.0.5", 10, null, null, 30, null)
            ]),
            Settings = new PalworldRestSettings(32, "Settings name", "Settings description"),
            Metrics = new PalworldRestMetrics(58, 1, 16.7, 32, 3661, 4, 9)
        };
        var service = CreateOverviewService(restService);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("healthy", result.Health);
        Assert.Equal("Online", result.HealthLabel);
        Assert.Equal("Amigos e Amigos", result.ServerName);
        Assert.Equal("v1.0.3", result.Version);
        Assert.Equal(1, result.OnlinePlayers);
        Assert.Equal(32, result.MaxPlayers);
        Assert.Equal(3661, result.UptimeSeconds);
        Assert.Equal(58, result.ServerFps);

        var player = Assert.Single(result.Players);
        Assert.Equal("Alek", player.Name);
        Assert.Equal("00000000", player.PublicId);
    }

    [Fact]
    public async Task OverviewDoesNotExposeCredentialsOrSensitivePlayerFields()
    {
        var restService = new FakePalworldRestService
        {
            Players = new PalworldRestPlayers(
            [
                new PalworldRestPlayer("Alek", "alek-account", "player-private-12345678", "steam_secret", "10.0.0.5", 10, null, null, 30, null)
            ])
        };
        var service = CreateOverviewService(restService);

        var result = await service.GetOverviewAsync(CancellationToken.None);
        var player = Assert.Single(result.Players);

        Assert.DoesNotContain("admin-password", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("steam_secret", result.ToString(), StringComparison.Ordinal);
        Assert.Equal("12345678", player.PublicId);
    }

    [Fact]
    public async Task RunningContainerWithUnavailableRestReturnsRestUnavailableOverview()
    {
        var service = CreateOverviewService(
            new FakePalworldRestService
            {
                Exception = new PalworldRestUnavailableException("not ready")
            });

        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("rest-unavailable", result.Health);
        Assert.Equal("REST unavailable", result.HealthLabel);
        Assert.False(result.RestApiAvailable);
        Assert.Equal("running", result.ContainerState);
    }

    [Fact]
    public async Task StoppedContainerDoesNotCallRest()
    {
        var restService = new FakePalworldRestService();
        var containerService = new FakeContainerService("exited");
        var service = CreateOverviewService(restService, containerService);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("container-stopped", result.Health);
        Assert.Equal(0, restService.CallCount);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(5));
        var service = CreateRestService(handler);
        using var cancellationTokenSource = new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.GetInfoAsync(cancellationTokenSource.Token));
    }

    [Fact]
    public async Task OverviewEndpointReturnsGamesHudContract()
    {
        await using var factory = CreateFactory(new FakePalworldOverviewService());
        using var client = factory.CreateClient();

        var overview = await client.GetFromJsonAsync<PalworldOverviewResponse>("/api/palworld/overview");

        Assert.NotNull(overview);
        Assert.Equal("healthy", overview.Health);
        Assert.Equal("Amigos e Amigos", overview.ServerName);
        Assert.True(overview.RestApiAvailable);
    }

    [Fact]
    public async Task PlayersEndpointDoesNotReturnCredentialsOrSensitivePlayerFields()
    {
        await using var factory = CreateFactory(new FakePalworldOverviewService());
        using var client = factory.CreateClient();

        var content = await client.GetStringAsync("/api/palworld/players");

        Assert.Contains("\"name\":\"Alek\"", content, StringComparison.Ordinal);
        Assert.Contains("\"publicId\":\"12345678\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-password", content, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", content, StringComparison.Ordinal);
        Assert.DoesNotContain("steam_secret", content, StringComparison.Ordinal);
    }

    private static PalworldRestService CreateRestService(
        HttpMessageHandler handler,
        int timeoutSeconds = 5)
    {
        return new PalworldRestService(
            new HttpClient(handler),
            Options.Create(new PalworldOptions
            {
                RestApi = new PalworldRestApiOptions
                {
                    BaseUrl = "http://palworld.local:8212",
                    Username = "admin",
                    Password = "admin-password",
                    TimeoutSeconds = timeoutSeconds
                }
            }));
    }

    private static PalworldOverviewService CreateOverviewService(
        IPalworldRestService restService,
        IContainerService? containerService = null)
    {
        return new PalworldOverviewService(
            Options.Create(new PalworldOptions
            {
                ContainerName = "palworld-server",
                ConnectionAddress = "pal.example.test:8211",
                RestApi = new PalworldRestApiOptions
                {
                    BaseUrl = "http://palworld.local:8212",
                    Username = "admin",
                    Password = "admin-password"
                }
            }),
            containerService ?? new FakeContainerService("running"),
            restService,
            NullLogger<PalworldOverviewService>.Instance);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IPalworldOverviewService overviewService)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(overviewService);
                });
            });
    }

    private sealed class StaticJsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StaticJsonHandler(string json)
        {
            _json = json;
        }

        public string? AuthorizationScheme { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastMethod = request.Method;
            LastUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayedHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"version":"v1"}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakePalworldRestService : IPalworldRestService
    {
        public PalworldRestException? Exception { get; set; }

        public int CallCount { get; private set; }

        public PalworldRestInfo Info { get; set; } = new("v1.0.3", "Amigos e Amigos", "Private world", "world-guid");

        public PalworldRestPlayers Players { get; set; } = new([]);

        public PalworldRestSettings Settings { get; set; } = new(32, "Amigos e Amigos", "Private world");

        public PalworldRestMetrics Metrics { get; set; } = new(60, 0, 16.1, 32, 300, 3, 12);

        public Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken)
        {
            return Execute(Info);
        }

        public Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken)
        {
            return Execute(Players);
        }

        public Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken)
        {
            return Execute(Settings);
        }

        public Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken)
        {
            return Execute(Metrics);
        }

        public Task SaveWorldAsync(CancellationToken cancellationToken)
        {
            CallCount++;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.CompletedTask;
        }

        public Task AnnounceAsync(
            string message,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.CompletedTask;
        }

        private Task<TResponse> Execute<TResponse>(TResponse response)
        {
            CallCount++;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FakeContainerService : IContainerService
    {
        private readonly string _state;

        public FakeContainerService(string state)
        {
            _state = state;
        }

        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<ContainerResponse>>(Array.Empty<ContainerResponse>());
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerDetailsResponse?>(new ContainerDetailsResponse(
                containerId,
                containerId,
                "thijsvanloef/palworld-server-docker:latest",
                "image-id",
                _state,
                _state,
                "2026-01-01T00:00:00Z",
                "2026-01-01T00:00:00Z",
                "",
                0,
                "linux",
                "overlay2",
                [],
                [],
                [],
                new Dictionary<string, string>()));
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            string stream,
            string? search,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLogsResponse?>(null);
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLifecycleActionResponse?>(null);
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLifecycleActionResponse?>(null);
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLifecycleActionResponse?>(null);
        }
    }

    private sealed class FakePalworldOverviewService : IPalworldOverviewService
    {
        private readonly PalworldPlayerResponse[] _players =
        [
            new("Alek", "alek-account", "12345678", 10, 30)
        ];

        public Task<PalworldOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldOverviewResponse(
                "Amigos e Amigos",
                "Amigos e Amigos",
                "palworld-server",
                "running",
                "running",
                "healthy",
                "Online",
                "v1.0.3",
                "Private world",
                "pal.example.test:8211",
                1,
                32,
                300,
                60,
                16.1,
                3,
                12,
                true,
                null,
                _players,
                DateTimeOffset.UtcNow.ToString("O")));
        }

        public Task<PalworldPlayersResponse> GetPlayersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldPlayersResponse(
                1,
                32,
                _players,
                DateTimeOffset.UtcNow.ToString("O")));
        }
    }
}
