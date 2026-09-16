using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Docker.DotNet.Models;

namespace GamesHud.Api.Tests;

public sealed class ProvisioningOrchestratorFoundationTests
{
    [Fact]
    public async Task PersistedCommandReturnsBeforeWorkerExecutesAndWorkerFinalizesServerRunning()
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        using var callerCancellation = new CancellationTokenSource();
        var signal = new TrackingSignal(callerCancellation.Cancel);
        var definition = CreateDefinition();
        var plan = CreatePlan(root.Path);
        var service = new GameServerProvisioningService(
            new StaticPlanBuilder(plan, definition), store, operations, new MustNotExecuteEngine(), signal);

        var command = await service.StartProvisioningAsync(
            new("server-one", "test-game", "Server One"), callerCancellation.Token);

        Assert.True(command.Succeeded);
        Assert.Equal(ProvisioningOperationStatuses.Pending, command.Status);
        Assert.True(signal.Signalled);
        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Equal(ProvisioningOperationStatuses.Pending,
            (await operations.GetAsync(command.OperationId!))!.Status);

        var engine = new ProvisioningEngine(operations, ProvisioningStepIds.ExecutableFoundation
            .Select(id => (IProvisioningStep)new NoHostMutationProvisioningStep(id)),
            NullLogger<ProvisioningEngine>.Instance);
        var executor = CreateExecutor(database, store, operations, definition, engine, []);
        await executor.ExecuteEligibleAsync(CancellationToken.None);

        var completed = await operations.GetAsync(command.OperationId!);
        var server = await store.GetManagedServerAsync("server-one");
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, completed!.Status);
        Assert.False(completed.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.Running, server!.LifecycleState);
    }

    [Fact]
    public async Task StartupDiscoveryCompletesFinalizationWithoutRepeatingSteps()
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        var definition = new PalworldGameDefinition();
        var reservation = await store.ReserveProvisioningPlanAsync(CreateV2PersistencePlan(root.Path, definition));
        var operation = await database.ProvisioningOperations.Include(item => item.Steps)
            .SingleAsync(item => item.Id == reservation.ProvisioningOperationId);
        operation.Status = ProvisioningOperationStatuses.Running;
        operation.CurrentStep = ProvisioningStepIds.Complete;
        foreach (var step in operation.Steps)
        {
            step.Status = ProvisioningStepStatuses.Succeeded;
            step.Attempt = Math.Max(step.Attempt, 1);
        }
        operation.Version++;
        await database.SaveChangesAsync();

        var executor = CreateExecutor(database, store, operations, CreateDefinition(),
            new MustNotExecuteEngine(), []);
        await executor.ExecuteEligibleAsync(CancellationToken.None);

        Assert.Equal(ProvisioningOperationStatuses.Succeeded,
            (await operations.GetAsync(reservation.ProvisioningOperationId))!.Status);
        Assert.Equal(ManagedGameServerLifecycleStates.Running,
            (await store.GetManagedServerAsync("server-one"))!.LifecycleState);
    }

    [Fact]
    public async Task AmbiguousMutationBlocksAndRetainsActiveSlot()
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        var definition = new PalworldGameDefinition();
        var reservation = await store.ReserveProvisioningPlanAsync(CreateV2PersistencePlan(root.Path, definition));
        var snapshot = await operations.GetAsync(reservation.ProvisioningOperationId);
        await operations.ApplyCheckpointAsync(new(reservation.ProvisioningOperationId, snapshot!.Version,
            ProvisioningOperationStatuses.Running, ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage, ProvisioningStepStatuses.Running));

        var reconciler = new StaticReconciler(ProvisioningStepIds.PrepareStorage,
            ProvisioningReconciliationOutcomes.Ambiguous);
        var executor = CreateExecutor(database, store, operations, CreateDefinition(),
            new MustNotExecuteEngine(), [reconciler]);
        await executor.ExecuteEligibleAsync(CancellationToken.None);

        var blocked = await operations.GetAsync(reservation.ProvisioningOperationId);
        Assert.Equal(ProvisioningOperationStatuses.Failed, blocked!.Status);
        Assert.True(blocked.IsActive);
        Assert.Equal("reconciled_effect_ambiguous",
            blocked.Steps.Single(item => item.StepId == ProvisioningStepIds.PrepareStorage).ErrorCode);
        Assert.Equal(ManagedGameServerLifecycleStates.ProvisioningBlocked,
            (await store.GetManagedServerAsync("server-one"))!.LifecycleState);
        Assert.Equal(1, reconciler.CallCount);
    }

    [Fact]
    public async Task StorageAndPortContractsPersistDistinctRuntimeCoordinates()
    {
        using var root = TemporaryRoot.Create();
        var apiRoot = Path.Combine(root.Path, "api-root");
        var hostRoot = Path.Combine(root.Path, "daemon-root");
        var paths = new ManagedStoragePathBuilder(Options.Create(new StorageOptions
        {
            DataRoot = root.Path,
            ManagedApiRoot = apiRoot,
            ManagedHostRoot = hostRoot
        }));
        var layout = paths.CreateLayout(new GameServerId("server-one"));
        Assert.NotEqual(layout.DataRoot, layout.HostRoot);

        await using var database = CreateInitialized(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var plan = CreatePersistencePlan(root.Path) with
        {
            Ports =
            [
                new PortReservationPlan("game", "udp", 8211, 8212, true, PortExposures.Public),
                new PortReservationPlan("rest-api", "tcp", 8212, null, false, PortExposures.Internal)
            ],
            Storage = [new StorageReservationPlan("data", "servers/server-one/data",
                Path.Combine(apiRoot, "servers", "server-one", "data"),
                Path.Combine(hostRoot, "servers", "server-one", "data"))]
        };
        await store.ReserveProvisioningPlanAsync(plan);
        var server = await store.GetManagedServerAsync("server-one");
        var game = server!.PortReservations.Single(item => item.PortDefinitionId == "game");
        var rest = server.PortReservations.Single(item => item.PortDefinitionId == "rest-api");
        var storage = Assert.Single(server.StorageReservations);

        Assert.Equal(8211, game.ContainerPort);
        Assert.Equal(8212, game.HostPort);
        Assert.True(game.Published);
        Assert.Equal(8212, rest.ContainerPort);
        Assert.Null(rest.HostPort);
        Assert.False(rest.Published);
        Assert.StartsWith(apiRoot, storage.ApiPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(hostRoot, storage.HostPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProvisioningReconciliationOutcomes.EffectExists)]
    [InlineData(ProvisioningReconciliationOutcomes.EffectAbsent)]
    public async Task ProvenMutationRecoveryReconcilesAndContinues(string outcome)
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        var definition = new PalworldGameDefinition();
        var reservation = await store.ReserveProvisioningPlanAsync(CreateV2PersistencePlan(root.Path, definition));
        var snapshot = await operations.GetAsync(reservation.ProvisioningOperationId);
        await operations.ApplyCheckpointAsync(new(reservation.ProvisioningOperationId, snapshot!.Version,
            ProvisioningOperationStatuses.Running, ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage, ProvisioningStepStatuses.Running));
        var reconciler = new StaticReconciler(ProvisioningStepIds.PrepareStorage, outcome);
        var imageStore = new RuntimeImageIntentStore(database, new EfCorePersistenceTransactionBoundary(database));
        var steps = ProvisioningStepIds.ExecutableFoundation
            .Where(id => id != ProvisioningStepIds.AcquireImage)
            .Select(id => (IProvisioningStep)new NoHostMutationProvisioningStep(id))
            .Append(new VerifyImageStep(imageStore, reservation.ProvisioningOperationId, definition));
        var engine = new ProvisioningEngine(operations, steps,
            NullLogger<ProvisioningEngine>.Instance);
        var executor = CreateExecutor(database, store, operations, definition, engine, [reconciler]);

        await executor.ExecuteEligibleAsync(CancellationToken.None);

        var completed = await operations.GetAsync(reservation.ProvisioningOperationId);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, completed!.Status);
        Assert.Equal(ManagedGameServerLifecycleStates.Running,
            (await store.GetManagedServerAsync("server-one"))!.LifecycleState);
        Assert.Equal(1, reconciler.CallCount);
    }

    [Fact]
    public async Task InterruptedReadOnlyStepIsRetriedByStartupRecovery()
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan(root.Path));
        var record = await database.ProvisioningOperations.Include(item => item.Steps)
            .SingleAsync(item => item.Id == reservation.ProvisioningOperationId);
        foreach (var step in record.Steps.Where(item => item.Sequence is >= 4 and < 8))
        {
            step.Status = ProvisioningStepStatuses.Succeeded;
            step.Attempt = 1;
        }
        var health = record.Steps.Single(item => item.StepId == ProvisioningStepIds.VerifyHealth);
        health.Status = ProvisioningStepStatuses.Running;
        health.Attempt = 1;
        record.Status = ProvisioningOperationStatuses.Running;
        record.CurrentStep = health.StepId;
        record.Version++;
        await database.SaveChangesAsync();
        var engine = new ProvisioningEngine(operations, ProvisioningStepIds.ExecutableFoundation
            .Select(id => (IProvisioningStep)new NoHostMutationProvisioningStep(id)),
            NullLogger<ProvisioningEngine>.Instance);
        var executor = CreateExecutor(database, store, operations, CreateDefinition(), engine, []);

        await executor.ExecuteEligibleAsync(CancellationToken.None);

        var completed = await operations.GetAsync(reservation.ProvisioningOperationId);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, completed!.Status);
        Assert.Equal(2, completed.Steps.Single(item => item.StepId == ProvisioningStepIds.VerifyHealth).Attempt);
    }

    [Fact]
    public void DockerMappingUsesContainerPortForKeyAndHostPortOnlyForPublishedEndpoint()
    {
        var definition = new PalworldGameDefinition();
        var root = Path.Combine(Path.GetTempPath(), "gameshud-arch04-runtime");
        var specification = new RuntimeMutationSpecification(
            new GameServerId("server-one"), new GameId("palworld"), "operation-one", "docker",
            definition.LegacyRuntimeImages.Single(),
            [
                new RuntimePortBinding("game", "game", "udp", 8211, 8212, true, PortExposures.Public),
                new RuntimePortBinding("rest", "rest-api", "tcp", 8212, null, false, PortExposures.Internal)
            ],
            [new RuntimeStorageMount("data", "data", Path.Combine(root, "servers", "server-one", "data"),
                "/palworld", false, null, root)],
            [], definition.RuntimeEnvironment, new(1, 1024), RuntimeRestartPolicies.UnlessStopped,
            RuntimeNetworkPolicies.GamesHudManaged);
        var validated = new RuntimeMutationPolicy().Validate(specification, definition, root).Specification;
        Assert.NotNull(validated);
        var mapped = DockerCreateContainerMapper.Map(new RuntimeMutationExecutionContext(validated!,
            RuntimeMutationKind.CreateRuntime, ProvisioningStepIds.CreateRuntime, 1));

        Assert.Contains("8211/udp", mapped.ExposedPorts.Keys);
        Assert.Contains("8212/tcp", mapped.ExposedPorts.Keys);
        Assert.Equal("8212", Assert.Single(mapped.HostConfig.PortBindings["8211/udp"]).HostPort);
        Assert.DoesNotContain("8212/tcp", mapped.HostConfig.PortBindings.Keys);
    }

    [Fact]
    public async Task KnownTerminalFailureReleasesSlotAndMarksServerFailed()
    {
        using var root = TemporaryRoot.Create();
        await using var database = CreateInitialized(root.Path);
        var paths = CreatePaths(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database), paths);
        var operations = CreateOperations(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan(root.Path));
        var loader = new ProvisioningContextLoader(operations, store,
            new GameDefinitionRegistry([CreateDefinition()]));
        var engine = new ProvisioningEngine(operations,
            ProvisioningStepIds.ExecutableFoundation.Select(id => (IProvisioningStep)(id == ProvisioningStepIds.PrepareStorage
                ? new FailedStep(id)
                : new NoHostMutationProvisioningStep(id))),
            NullLogger<ProvisioningEngine>.Instance);

        await engine.ExecuteAsync(await loader.LoadAsync(reservation.ProvisioningOperationId, CancellationToken.None),
            CancellationToken.None);

        var failed = await operations.GetAsync(reservation.ProvisioningOperationId);
        Assert.Equal(ProvisioningOperationStatuses.Failed, failed!.Status);
        Assert.False(failed.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.ProvisioningFailed,
            (await store.GetManagedServerAsync("server-one"))!.LifecycleState);
    }

    [Fact]
    public async Task MigrationUpgradesHistoricalPortRowsWithoutReinterpretingInternalPublication()
    {
        using var root = TemporaryRoot.Create();
        await using var database = new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root.Path, "upgrade.db")};Pooling=False").Options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260915122024_AddDurableRuntimeImageIdentity");
        await database.Database.ExecuteSqlRawAsync("""
            INSERT INTO managed_game_servers
                (Id, GameId, DisplayName, InstallationType, RuntimeType, LifecycleState, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('historical', 'test-game', 'Historical', 'managed', 'docker', 'pending_provisioning', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO provisioning_operations
                (Id, GameServerId, Type, Status, ActiveSlot, CurrentStep, PipelineVersion, Version, StartedAtUtc, UpdatedAtUtc)
            VALUES ('op-historical', 'historical', 'provision', 'running', 'active', 'reserve_resources', 'gh09-v1', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO port_reservations
                (Id, GameServerId, PortDefinitionId, Protocol, Port, Exposure, Status, ProvisioningOperationId, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('port-public', 'historical', 'game', 'udp', 8211, 'public', 'reserved', 'op-historical', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                   ('port-internal', 'historical', 'rest', 'tcp', 8212, 'internal', 'reserved', 'op-historical', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO storage_reservations
                (Id, GameServerId, StorageDefinitionId, RelativePath, Ownership, Status, ProvisioningOperationId, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('storage-data', 'historical', 'data', 'servers/historical/data', 'managed', 'reserved', 'op-historical', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """);

        await migrator.MigrateAsync();
        var publicPort = await database.PortReservations.AsNoTracking().SingleAsync(item => item.Id == "port-public");
        var internalPort = await database.PortReservations.AsNoTracking().SingleAsync(item => item.Id == "port-internal");
        var storage = await database.StorageReservations.AsNoTracking().SingleAsync();
        Assert.Equal(8211, publicPort.ContainerPort);
        Assert.Equal(8211, publicPort.HostPort);
        Assert.True(publicPort.Published);
        Assert.Equal(8212, internalPort.ContainerPort);
        Assert.Null(internalPort.HostPort);
        Assert.False(internalPort.Published);
        Assert.Equal(string.Empty, storage.ApiPath);
        Assert.Equal(string.Empty, storage.HostPath);
    }

    private static ProvisioningOperationExecutor CreateExecutor(
        GamesHudDbContext database,
        IManagedServerStore store,
        IProvisioningOperationStore operations,
        GameDefinition definition,
        IProvisioningEngine engine,
        IEnumerable<IProvisioningStepReconciler> reconcilers)
    {
        var recovery = new ProvisioningRecoveryService(operations);
        var reconciliation = new ProvisioningReconciliationService(operations,
            new EfCorePersistenceTransactionBoundary(database), reconcilers);
        var loader = new ProvisioningContextLoader(operations, store,
            new GameDefinitionRegistry([definition]));
        return new(operations, recovery, reconciliation, loader, engine,
            NullLogger<ProvisioningOperationExecutor>.Instance);
    }

    private static ValidatedProvisioningPlan CreatePlan(string root) => new(
        new GameServerId("server-one"), new GameId("test-game"), "Server One", "docker", "compatible", [],
        [new ValidatedProvisioningPort("game", "udp", 8211, 8212, true, PortExposures.Public)],
        [new ValidatedProvisioningStorage("data", "servers/server-one/data",
            Path.Combine(root, "api", "servers", "server-one", "data"),
            Path.Combine(root, "host", "servers", "server-one", "data"))],
        [], ProvisioningStepIds.All);

    private static ManagedServerProvisioningPlan CreatePersistencePlan(string root) => new(
        "server-one", "test-game", "Server One", "docker",
        [new PortReservationPlan("game", "udp", 8211, 8211, true, PortExposures.Public)],
        [new StorageReservationPlan("data", "servers/server-one/data",
            Path.Combine(root, "api", "servers", "server-one", "data"),
            Path.Combine(root, "host", "servers", "server-one", "data"))]);

    private static ManagedServerProvisioningPlan CreateV2PersistencePlan(string root, PalworldGameDefinition definition) => new(
        "server-one", "palworld", "Server One", "docker",
        [new PortReservationPlan("game", "udp", 8211, 8211, true, PortExposures.Public)],
        [new StorageReservationPlan("data", "servers/server-one/data",
            Path.Combine(root, "api", "servers", "server-one", "data"),
            Path.Combine(root, "host", "servers", "server-one", "data"))],
        PipelineVersion: ProvisioningPipelines.ImageAcquisitionVersion,
        Steps: ProvisioningPipelines.ImageAcquisition.Steps.Select(step => new ProvisioningStepPlan(
            step.Id, step.Sequence, step.RetryClassification, step.SideEffectClassification,
            step.MaxAttempts, step.Sequence <= 3)).ToArray(),
        RuntimeImage: definition.RuntimeImages.Single());

    private static GameDefinition CreateDefinition() => new(
        new GameId("test-game"), "Test Game", "Test", new GameDefinitionBranding("test"),
        ["docker"], ["test"], ports: [new GamePortDefinition("game", "Game", 8211, "udp", true, true,
            PortExposures.Public, "Game", "test")],
        storages: [new GameStorageDefinition("data", "Data", StoragePurposes.GameData, "/data", true, true, true, true, null, "test")],
        legacyRuntimeImages: [new TrustedRuntimeImage("docker", "example/test", "stable", "test")]);

    private static IManagedStoragePathBuilder CreatePaths(string root) =>
        new ManagedStoragePathBuilder(Options.Create(new StorageOptions
        {
            DataRoot = root,
            ManagedApiRoot = Path.Combine(root, "api"),
            ManagedHostRoot = Path.Combine(root, "host")
        }));

    private static ProvisioningOperationStore CreateOperations(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database), new ProvisioningStateMachine());

    private static GamesHudDbContext CreateInitialized(string root)
    {
        var context = new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "gameshud.db")};Pooling=False").Options);
        context.Database.Migrate();
        return context;
    }

    private sealed class StaticPlanBuilder(ValidatedProvisioningPlan plan, GameDefinition definition)
        : IProvisioningPlanBuilder
    {
        public Task<ProvisioningPlanBuildResult> BuildAsync(CreateGameServerProvisioningRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new ProvisioningPlanBuildResult(plan, definition, null));
    }

    private sealed class TrackingSignal(Action? onSignal = null) : IProvisioningExecutionSignal
    {
        public bool Signalled { get; private set; }
        public void Signal()
        {
            Signalled = true;
            onSignal?.Invoke();
        }
        public Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MustNotExecuteEngine : IProvisioningEngine
    {
        public Task<ProvisioningExecutionResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Execution was not expected.");
    }

    private sealed class FailedStep(string id) : IProvisioningStep
    {
        public string Id => id;
        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromResult(ProvisioningStepResult.Failure("test_failure", "Test failure."));
    }

    private sealed class StaticReconciler(string stepId, string outcome) : IProvisioningStepReconciler
    {
        public string StepId => stepId;
        public int CallCount { get; private set; }
        public Task<ProvisioningReconciliationResult> InspectAsync(ProvisioningOperationSnapshot operation,
            ProvisioningStepSnapshot step, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ProvisioningReconciliationResult(outcome, "Test evidence."));
        }
    }

    private sealed class VerifyImageStep(
        IRuntimeImageIntentStore store,
        string operationId,
        PalworldGameDefinition definition) : IProvisioningStep
    {
        public string Id => ProvisioningStepIds.AcquireImage;

        public async Task<ProvisioningStepResult> ExecuteAsync(
            ProvisioningContext context,
            CancellationToken cancellationToken)
        {
            var owner = new RuntimeImageOwner(operationId, "server-one", "palworld", "docker");
            var intent = await store.LoadAsync(owner, cancellationToken);
            await store.RecordVerifiedAsync(owner, definition.RuntimeImages.Single(),
                new LocalImageId($"sha256:{new string('a', 64)}"), intent.Version, cancellationToken);
            return ProvisioningStepResult.Success();
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        private TemporaryRoot(string path) => Path = path;
        public string Path { get; }
        public static TemporaryRoot Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshud-arch04", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
