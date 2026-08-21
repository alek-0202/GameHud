using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GamesHud.Api.Tests;

public sealed class GamePortsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void NetworkPortAcceptsValidRange(int port)
    {
        var networkPort = new NetworkPort(port, PortProtocols.Tcp);

        Assert.Equal(port, networkPort.Number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void NetworkPortRejectsInvalidRange(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NetworkPort(port, PortProtocols.Tcp));
    }

    [Fact]
    public void NetworkPortRejectsUnsupportedProtocol()
    {
        Assert.Throws<ArgumentException>(() => new NetworkPort(8211, "sctp"));
    }

    [Fact]
    public void TcpAndUdpWithSameNumberAreDistinctPorts()
    {
        Assert.NotEqual(
            new NetworkPort(8211, PortProtocols.Tcp),
            new NetworkPort(8211, PortProtocols.Udp));
    }

    [Fact]
    public void DuplicateLogicalPortDefinitionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new GameDefinition(
                new GameId("duplicate-ports"),
                "Duplicate Ports",
                "Duplicate ports test.",
                new GameDefinitionBranding("duplicate-ports"),
                [GameServerRuntime.DockerType],
                [GameServerCapabilities.Overview],
                ports:
                [
                    CreatePortDefinition("game", 8211),
                    CreatePortDefinition("GAME", 8212)
                ]));
    }

    [Fact]
    public void PalworldPortDefinitionsUseDocumentedPortsAndExposure()
    {
        var definition = new PalworldGameDefinition();

        Assert.Equal(3, definition.Ports.Count);
        AssertPort(definition, "game", 8211, PortProtocols.Udp, PortExposures.Public, required: true);
        AssertPort(definition, "query", 27015, PortProtocols.Udp, PortExposures.Public, required: false);
        AssertPort(definition, "rest-api", 8212, PortProtocols.Tcp, PortExposures.Internal, required: false);
    }

    [Fact]
    public async Task TcpAvailablePortIsReportedAvailable()
    {
        var port = GetFreeTcpPort();
        var service = new PortAvailabilityService(new FakeDockerPublishedPortProvider([]));

        var availability = await service.CheckAvailabilityAsync(
            new NetworkPort(port, PortProtocols.Tcp),
            CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Equal(PortAvailabilityStatuses.Available, availability.Status);
    }

    [Fact]
    public async Task TcpUnavailablePortIsReportedInUse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var service = new PortAvailabilityService(new FakeDockerPublishedPortProvider([]));

        var availability = await service.CheckAvailabilityAsync(
            new NetworkPort(port, PortProtocols.Tcp),
            CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Equal(PortAvailabilityStatuses.InUse, availability.Status);
    }

    [Fact]
    public async Task UdpAvailablePortIsReportedAvailable()
    {
        var port = GetFreeUdpPort();
        var service = new PortAvailabilityService(new FakeDockerPublishedPortProvider([]));

        var availability = await service.CheckAvailabilityAsync(
            new NetworkPort(port, PortProtocols.Udp),
            CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Equal(PortAvailabilityStatuses.Available, availability.Status);
    }

    [Fact]
    public async Task UdpUnavailablePortIsReportedInUse()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        var service = new PortAvailabilityService(new FakeDockerPublishedPortProvider([]));

        var availability = await service.CheckAvailabilityAsync(
            new NetworkPort(port, PortProtocols.Udp),
            CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Equal(PortAvailabilityStatuses.InUse, availability.Status);
    }

    [Fact]
    public async Task DockerPublishedPortIsDiagnosticOnly()
    {
        var port = GetFreeTcpPort();
        var service = new PortAvailabilityService(new FakeDockerPublishedPortProvider(
            [new DockerPublishedPort(new NetworkPort(port, PortProtocols.Tcp), "container-id", "private-name")]));

        var availability = await service.CheckAvailabilityAsync(
            new NetworkPort(port, PortProtocols.Tcp),
            CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Single(availability.DockerPublishedPorts);
    }

    [Fact]
    public async Task PreferredPortAvailableIsAllocated()
    {
        var result = await AllocateAsync(availablePorts: [8211]);

        Assert.Equal(PortAllocationStatuses.Allocated, result.Status);
        Assert.Equal(8211, result.AllocatedPort?.Number);
        Assert.False(result.UsedAlternative);
    }

    [Fact]
    public async Task PreferredOccupiedWithAlternativeAllowedReturnsAlternative()
    {
        var result = await AllocateAsync(availablePorts: [8213]);

        Assert.Equal(PortAllocationStatuses.Allocated, result.Status);
        Assert.Equal(8213, result.AllocatedPort?.Number);
        Assert.True(result.UsedAlternative);
        Assert.Contains(result.CheckedPorts, port => port.Number == 8211);
        Assert.Contains(result.CheckedPorts, port => port.Number == 8212);
    }

    [Fact]
    public async Task PreferredOccupiedWithAlternativeForbiddenFails()
    {
        var result = await AllocateAsync(availablePorts: [], allowAlternative: false);

        Assert.Equal(PortAllocationStatuses.Failed, result.Status);
        Assert.Equal(PortErrorCodes.PortInUse, result.ErrorCode);
        Assert.Null(result.AllocatedPort);
    }

    [Fact]
    public async Task MultipleAlternativesOccupiedSkipsToFirstAvailable()
    {
        var result = await AllocateAsync(availablePorts: [8215]);

        Assert.Equal(PortAllocationStatuses.Allocated, result.Status);
        Assert.Equal(8215, result.AllocatedPort?.Number);
        Assert.True(result.UsedAlternative);
    }

    [Fact]
    public async Task NoAlternativeFoundFailsWithFriendlyError()
    {
        var result = await AllocateAsync(availablePorts: []);

        Assert.Equal(PortAllocationStatuses.Failed, result.Status);
        Assert.Equal(PortErrorCodes.NoAlternativePort, result.ErrorCode);
        Assert.Null(result.AllocatedPort);
    }

    [Fact]
    public async Task InProcessCoordinationDoesNotReturnSameConcurrentCandidate()
    {
        var availabilityService = new FakePortAvailabilityService(port => port.Number is 30000 or 30001);
        var allocator = new PortAllocator(availabilityService);
        var request = CreateAllocationRequest(30000);

        var results = await Task.WhenAll(
            allocator.AllocateAsync(request, CancellationToken.None),
            allocator.AllocateAsync(request, CancellationToken.None));

        Assert.Equal([30000, 30001], results.Select(result => result.AllocatedPort?.Number).Order().ToArray());
    }

    [Fact]
    public async Task PlannerReturnsReadyWithAlternativesWhenPreferredPortIsOccupied()
    {
        var planner = CreatePlanner(availablePorts: [8212]);
        var definition = CreateDefinition(ports: [CreatePortDefinition("game", 8211)]);

        var plan = await planner.CreatePlanAsync(definition, CancellationToken.None);

        var item = Assert.Single(plan.Ports);
        Assert.Equal(PortPlanStatuses.ReadyWithAlternatives, plan.Status);
        Assert.Equal(8212, item.Allocation.AllocatedPort?.Number);
    }

    [Fact]
    public async Task PlannerReturnsConflictForRequiredPortWithoutAlternative()
    {
        var planner = CreatePlanner(availablePorts: []);
        var definition = CreateDefinition(ports: [
            CreatePortDefinition("game", 8211, required: true, allowAlternative: false)
        ]);

        var plan = await planner.CreatePlanAsync(definition, CancellationToken.None);

        Assert.Equal(PortPlanStatuses.Conflict, plan.Status);
    }

    [Fact]
    public async Task PlannerReturnsUnknownWhenDefinitionHasNoPorts()
    {
        var planner = CreatePlanner(availablePorts: []);

        var plan = await planner.CreatePlanAsync(CreateDefinition(ports: []), CancellationToken.None);

        Assert.Equal(PortPlanStatuses.Unknown, plan.Status);
    }

    [Fact]
    public async Task SystemPortEndpointReturnsAvailability()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<PortAvailabilityResponse>("/api/system/ports/udp/8211");

        Assert.NotNull(response);
        Assert.Equal(8211, response.Port);
        Assert.Equal(PortProtocols.Udp, response.Protocol);
        Assert.True(response.Available);
    }

    [Theory]
    [InlineData("/api/system/ports/udp/0", PortErrorCodes.InvalidPort)]
    [InlineData("/api/system/ports/udp/65536", PortErrorCodes.InvalidPort)]
    [InlineData("/api/system/ports/sctp/8211", PortErrorCodes.UnsupportedProtocol)]
    public async Task SystemPortEndpointReturnsFriendlyErrors(string path, string expectedCode)
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedCode, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GamePortPlanEndpointReturnsPlanForKnownGame()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        var plan = await client.PostAsync("/api/games/palworld/ports/plan", null);
        var response = await plan.Content.ReadFromJsonAsync<GamePortPlanResponse>();

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.NotNull(response);
        Assert.Equal("palworld", response.GameId);
        Assert.Contains(response.Ports, port => port.DefinitionId == "rest-api"
            && port.Exposure == PortExposures.Internal);
    }

    [Fact]
    public async Task GamePortPlanEndpointReturnsNotFoundForUnknownGame()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/games/unknown/ports/plan", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void PortPlanResponseDoesNotExposeSensitiveProcessOrContainerDetails()
    {
        var response = GamePortContractMapper.Map(new GamePortPlan(
            "test-game",
            "Test Game",
            PortPlanStatuses.Ready,
            [
                new GamePortPlanItem(
                    "game",
                    "Game port",
                    "Player traffic",
                    PortExposures.Public,
                    true,
                    true,
                    new PortAvailability(
                        new NetworkPort(8211, PortProtocols.Udp),
                        PortAvailabilityStatuses.Available,
                        true,
                        [new DockerPublishedPort(new NetworkPort(8211, PortProtocols.Udp), "secret-container-id", "private-container")],
                        "Port appears available on this host."),
                    new PortAllocationResult(
                        new NetworkPort(8211, PortProtocols.Udp),
                        new NetworkPort(8211, PortProtocols.Udp),
                        false,
                        PortAllocationStatuses.Allocated,
                        null,
                        "Preferred port is available.",
                        [new NetworkPort(8211, PortProtocols.Udp)]))
            ],
            "Port availability is advisory until durable reservation exists."));
        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("Process", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Owner", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-container-id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-container", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PortAllocationResult> AllocateAsync(
        IReadOnlyCollection<int> availablePorts,
        bool allowAlternative = true)
    {
        var allocator = new PortAllocator(new FakePortAvailabilityService(port =>
            availablePorts.Contains(port.Number)));

        return await allocator.AllocateAsync(
            CreateAllocationRequest(8211, allowAlternative),
            CancellationToken.None);
    }

    private static PortAllocationRequest CreateAllocationRequest(
        int preferredPort,
        bool allowAlternative = true)
    {
        return new PortAllocationRequest(
            new GameServerId("test-server"),
            "game",
            new NetworkPort(preferredPort, PortProtocols.Udp),
            allowAlternative);
    }

    private static PortPlanner CreatePlanner(IReadOnlyCollection<int> availablePorts)
    {
        var availabilityService = new FakePortAvailabilityService(port =>
            availablePorts.Contains(port.Number));

        return new PortPlanner(availabilityService, new PortAllocator(availabilityService));
    }

    private static GameDefinition CreateDefinition(IReadOnlyCollection<GamePortDefinition> ports)
    {
        return new GameDefinition(
            new GameId("test-game"),
            "Test Game",
            "Test game support.",
            new GameDefinitionBranding("test-game"),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview],
            ports: ports);
    }

    private static GamePortDefinition CreatePortDefinition(
        string id,
        int port,
        bool required = true,
        bool allowAlternative = true)
    {
        return new GamePortDefinition(
            id,
            "Game port",
            port,
            PortProtocols.Udp,
            required,
            allowAlternative,
            PortExposures.Public,
            "Player traffic");
    }

    private static WebApplicationFactory<Program> CreateFactory(IGameDefinitionRegistry registry)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(registry);
                    services.AddSingleton<IDockerPublishedPortProvider>(_ =>
                        new FakeDockerPublishedPortProvider([]));
                    services.AddSingleton<IPortAvailabilityService>(_ =>
                        new FakePortAvailabilityService(_ => true));
                });
            });
    }

    private static void AssertPort(
        PalworldGameDefinition definition,
        string id,
        int number,
        string protocol,
        string exposure,
        bool required)
    {
        var port = definition.Ports.Single(candidate => candidate.Id == id);

        Assert.Equal(number, port.DefaultPort.Number);
        Assert.Equal(protocol, port.DefaultPort.Protocol);
        Assert.Equal(exposure, port.Exposure);
        Assert.Equal(required, port.Required);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private sealed class FakePortAvailabilityService : IPortAvailabilityService
    {
        private readonly Func<NetworkPort, bool> _isAvailable;

        public FakePortAvailabilityService(Func<NetworkPort, bool> isAvailable)
        {
            _isAvailable = isAvailable;
        }

        public Task<PortAvailability> CheckAvailabilityAsync(
            NetworkPort port,
            CancellationToken cancellationToken)
        {
            var isAvailable = _isAvailable(port);

            return Task.FromResult(new PortAvailability(
                port,
                isAvailable ? PortAvailabilityStatuses.Available : PortAvailabilityStatuses.InUse,
                isAvailable,
                [],
                isAvailable
                    ? "Port appears available on this host."
                    : "Port is already in use on this host."));
        }
    }

    private sealed class FakeDockerPublishedPortProvider : IDockerPublishedPortProvider
    {
        private readonly IReadOnlyCollection<DockerPublishedPort> _ports;

        public FakeDockerPublishedPortProvider(IReadOnlyCollection<DockerPublishedPort> ports)
        {
            _ports = ports;
        }

        public Task<IReadOnlyCollection<DockerPublishedPort>> GetPublishedPortsAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_ports);
        }
    }
}
