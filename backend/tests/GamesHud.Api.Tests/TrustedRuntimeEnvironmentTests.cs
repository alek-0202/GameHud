using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class TrustedRuntimeEnvironmentTests
{
    [Fact]
    public async Task RuntimeSpecificationBuilderCopiesOnlyDefinitionEnvironment()
    {
        var definition = new PalworldGameDefinition();
        var server = CreateManagedServer();
        var builder = new RuntimeSpecificationBuilder(
            new FakeManagedServerStore(server),
            new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = CreateRoot() })),
            new GameDefinitionRegistry([definition]));
        var plan = new ValidatedProvisioningPlan(
            new GameServerId("server-one"), definition.GameId, "Server one", "docker", "compatible", [],
            [new ValidatedProvisioningPort("game", PortProtocols.Udp, 8211, PortExposures.Public)],
            [new ValidatedProvisioningStorage("data", "servers/server-one/data")], [], ProvisioningStepIds.All);
        var reservation = new ManagedServerReservationResult(
            "server-one", "operation-one", ["port-one"], ["storage-one"]);

        var specification = await builder.BuildAsync(
            new ProvisioningContext("operation-one", definition, plan, reservation), CancellationToken.None);

        var environment = Assert.Single(specification!.Environment);
        Assert.Same(Assert.Single(definition.RuntimeEnvironment), environment);
        Assert.Equal("DISABLE_GENERATE_SETTINGS", environment.Name);
        Assert.Equal("true", environment.Value);
    }

    [Fact]
    public void ProvisioningRequestHasNoEnvironmentInputSurface()
    {
        Assert.DoesNotContain(typeof(CreateGameServerProvisioningRequest).GetProperties(), property =>
            property.Name.Contains("environment", StringComparison.OrdinalIgnoreCase)
            || property.Name.Equals("env", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("variable", StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedGameServerRecord CreateManagedServer()
    {
        var server = new ManagedGameServerRecord
        {
            Id = "server-one",
            GameId = "palworld",
            DisplayName = "Server one",
            InstallationType = ManagedInstallationTypes.Managed,
            RuntimeType = "docker"
        };
        server.PortReservations.Add(new PortReservationRecord
        {
            Id = "port-one",
            GameServerId = server.Id,
            PortDefinitionId = "game",
            Protocol = PortProtocols.Udp,
            Port = 8211,
            Exposure = PortExposures.Public,
            Status = ReservationStatuses.Reserved,
            ProvisioningOperationId = "operation-one"
        });
        server.StorageReservations.Add(new StorageReservationRecord
        {
            Id = "storage-one",
            GameServerId = server.Id,
            StorageDefinitionId = "data",
            RelativePath = "servers/server-one/data",
            Ownership = StorageOwnerships.Managed,
            Status = ReservationStatuses.Reserved,
            ProvisioningOperationId = "operation-one"
        });
        return server;
    }

    private static string CreateRoot() =>
        Path.Combine(Path.GetTempPath(), "gameshud-runtime-environment-tests", Guid.NewGuid().ToString("N"));

    private sealed class FakeManagedServerStore(ManagedGameServerRecord server) : IManagedServerStore
    {
        public Task<ManagedGameServerRecord?> GetManagedServerAsync(string gameServerId,
            CancellationToken cancellationToken = default) => Task.FromResult<ManagedGameServerRecord?>(server);

        public Task<ManagedServerReservationResult> ReserveProvisioningPlanAsync(ManagedServerProvisioningPlan plan,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProvisioningOperationRecord?> GetActiveOperationAsync(string gameServerId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManagedServerReservationConflict?> FindReservationConflictAsync(ManagedServerProvisioningPlan plan,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
