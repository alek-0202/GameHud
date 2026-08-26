using System.Text.Json;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.HostCapabilities.Services;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Secrets.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class ProvisioningTests
{
    [Fact]
    public void RequestContractCannotSupplyHostOrRuntimeMutationInputs()
    {
        var properties = typeof(CreateGameServerProvisioningRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["GameServerId", "GameId", "DisplayName"], properties);
        Assert.DoesNotContain(properties, name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Mount", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Privileged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatedPlanAllowsOnlyOpaqueSecretReferences()
    {
        var reference = new SecretReference(SecretId.New());
        var plan = CreateValidatedPlan("server-one", 8211, "servers/server-one/data") with
        {
            SecretReferences = [reference]
        };
        var propertyTypes = typeof(ValidatedProvisioningPlan)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Equal(reference, Assert.Single(plan.SecretReferences));
        Assert.DoesNotContain(typeof(SecretValue), propertyTypes);
    }

    [Fact]
    public async Task PlanBuilderReusesRequirementsAndPlannersAndAcceptsHostWarnings()
    {
        var definition = new PalworldGameDefinition();
        var builder = CreatePlanBuilder(definition, CreateCompatibleHost(logicalProcessors: 2));

        var result = await builder.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "palworld", "Server One"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(GameCompatibilityStatuses.CompatibleWithWarnings, result.Plan!.HostCompatibilityStatus);
        Assert.NotEmpty(result.Plan.HostWarnings);
        Assert.NotEmpty(result.Plan.Ports);
        Assert.NotEmpty(result.Plan.Storage);
        Assert.All(result.Plan.Storage, item => Assert.StartsWith("servers/server-one/", item.RelativePath));
        Assert.Empty(result.Plan.SecretReferences);
        Assert.Equal(ProvisioningStepIds.All, result.Plan.RequiredSteps);
    }

    [Fact]
    public async Task PlanBuilderRejectsUnknownGameInvalidServerIdAndIncompatibleHost()
    {
        var definition = new PalworldGameDefinition();
        var compatible = CreatePlanBuilder(definition, CreateCompatibleHost());
        var incompatible = CreatePlanBuilder(definition, CreateCompatibleHost(architecture: "arm64"));

        var unknown = await compatible.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "unknown", "Server"),
            CancellationToken.None);
        var invalid = await compatible.BuildAsync(
            new CreateGameServerProvisioningRequest("../escape", "palworld", "Server"),
            CancellationToken.None);
        var hostFailure = await incompatible.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "palworld", "Server"),
            CancellationToken.None);

        Assert.Equal(ProvisioningErrorCodes.GameNotFound, unknown.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.InvalidGameServerId, invalid.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.HostIncompatible, hostFailure.Failure!.Code);
    }

    [Fact]
    public async Task PreviewDoesNotReserveOrMutateHost()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        var preview = await harness.Service.PreviewAsync(CreateRequest("preview"), CancellationToken.None);

        Assert.True(preview.IsValid);
        Assert.False(await database.ManagedGameServers.AnyAsync());
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task SuccessfulFoundationPersistsReservationProgressAndTerminalStatus()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        var result = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var operation = await harness.Store.GetOperationAsync(result.OperationId!);
        var server = await harness.Store.GetManagedServerAsync("server-one");

        Assert.True(result.Succeeded);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation!.Status);
        Assert.Equal(ProvisioningStepIds.Complete, operation.CurrentStep);
        Assert.Null(operation.ActiveSlot);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Equal(ManagedGameServerLifecycleStates.PendingProvisioning, server!.LifecycleState);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task DuplicateActiveAndCrossServerResourceConflictsAreSafe()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        await harness.Store.ReserveProvisioningPlanAsync(CreatePersistencePlan("active", 8301, "servers/active/data"));
        var active = await harness.Service.StartProvisioningAsync(CreateRequest("active"), CancellationToken.None);
        var first = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var duplicate = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var portConflictHarness = CreateHarness(database, _ => CreateValidatedPlan("server-two", 8211, "servers/server-two/data"));
        var portConflict = await portConflictHarness.Service.StartProvisioningAsync(CreateRequest("server-two"), CancellationToken.None);
        var storageConflictHarness = CreateHarness(database, _ => CreateValidatedPlan("server-three", 8403, "servers/server-one/data"));
        var storageConflict = await storageConflictHarness.Service.StartProvisioningAsync(CreateRequest("server-three"), CancellationToken.None);

        Assert.Equal(ProvisioningErrorCodes.OperationInProgress, active.Failure!.Code);
        Assert.True(first.Succeeded);
        Assert.Equal(ProvisioningErrorCodes.DuplicateServer, duplicate.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.PortConflict, portConflict.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.StorageConflict, storageConflict.Failure!.Code);
    }

    [Fact]
    public async Task FailedStepPersistsSafeErrorAndRunsCompensation()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var compensated = false;
        var steps = CreateSteps(
            new FakeStep(ProvisioningStepIds.PrepareStorage, ProvisioningStepResult.Success(), () => compensated = true),
            new FakeStep(ProvisioningStepIds.ConfigureGame, exception: new InvalidOperationException("TEST-ONLY-SECRET-MUST-NOT-PERSIST")));
        var engine = new ProvisioningEngine(store, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await store.GetOperationAsync(reservation.ProvisioningOperationId);
        var databaseJson = JsonSerializer.Serialize(await database.ProvisioningOperations.AsNoTracking().ToArrayAsync());

        Assert.False(result.Succeeded);
        Assert.True(compensated);
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation!.Status);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, operation.CurrentStep);
        Assert.Equal(ProvisioningErrorCodes.StepFailed, operation.ErrorCode);
        Assert.DoesNotContain("TEST-ONLY-SECRET", databaseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StepObservesRunningCurrentStepAndTypedFailureStopsPipeline()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var pending = await store.GetOperationAsync(reservation.ProvisioningOperationId);
        var observedRunning = false;
        var steps = CreateSteps(
            new ObservingStep(ProvisioningStepIds.PrepareStorage, async () =>
            {
                var running = await store.GetOperationAsync(reservation.ProvisioningOperationId);
                observedRunning = running!.Status == ProvisioningOperationStatuses.Running
                    && running.CurrentStep == ProvisioningStepIds.PrepareStorage;
                return ProvisioningStepResult.Success();
            }),
            new FakeStep(
                ProvisioningStepIds.ConfigureGame,
                ProvisioningStepResult.Failure("configuration_failed", "Configuration validation failed.")));
        var engine = new ProvisioningEngine(store, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var failed = await store.GetOperationAsync(reservation.ProvisioningOperationId);

        Assert.Equal(ProvisioningOperationStatuses.Pending, pending!.Status);
        Assert.True(observedRunning);
        Assert.False(result.Succeeded);
        Assert.Equal("configuration_failed", failed!.ErrorCode);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, failed.CurrentStep);
    }

    [Fact]
    public async Task CancellationPersistsFailedInsteadOfSucceeded()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var engine = new ProvisioningEngine(store, CreateSteps(), NullLogger<ProvisioningEngine>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await engine.ExecuteAsync(CreateContext(reservation), cancellation.Token);
        var operation = await store.GetOperationAsync(reservation.ProvisioningOperationId);

        Assert.False(result.Succeeded);
        Assert.Equal(ProvisioningErrorCodes.ProvisioningCancelled, operation!.ErrorCode);
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation.Status);
    }

    [Fact]
    public async Task InvalidTerminalTransitionIsRejected()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        await store.UpdateOperationAsync(new ProvisioningOperationUpdate(
            reservation.ProvisioningOperationId,
            ProvisioningOperationStatuses.Failed,
            ProvisioningStepIds.ConfigureGame,
            ProvisioningErrorCodes.StepFailed,
            "Safe failure."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateOperationAsync(
            new ProvisioningOperationUpdate(
                reservation.ProvisioningOperationId,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.CreateRuntime)));
    }

    [Fact]
    public async Task IncompleteOperationQuerySurvivesNewDbContext()
    {
        using var root = TemporaryDirectory.Create();
        await using (var database = CreateInitializedDbContext(root.Path))
        {
            await CreateStore(database).ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        }

        await using var reloaded = CreateDbContext(root.Path);
        var incomplete = await CreateStore(reloaded).GetIncompleteOperationsAsync();

        Assert.Single(incomplete);
        Assert.Equal(ProvisioningOperationStatuses.Pending, incomplete.Single().Status);
    }

    [Fact]
    public async Task DifferentServersCanCompleteWithoutGlobalLock()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var first = CreateHarness(database, _ => CreateValidatedPlan("one", 8501, "servers/one/data"));
        var second = CreateHarness(database, _ => CreateValidatedPlan("two", 8502, "servers/two/data"));

        var firstResult = await first.Service.StartProvisioningAsync(CreateRequest("one"), CancellationToken.None);
        var secondResult = await second.Service.StartProvisioningAsync(CreateRequest("two"), CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(2, await database.ManagedGameServers.CountAsync());
    }

    private static ProvisioningPlanBuilder CreatePlanBuilder(GameDefinition definition, HostCapabilitySnapshot host) =>
        new(
            new GameDefinitionRegistry([definition]),
            new StubHostCapabilityService(host),
            new GameRequirementEvaluator(),
            new StubPortPlanner(),
            new StubStoragePlanner());

    private static ProvisioningHarness CreateHarness(
        GamesHudDbContext database,
        Func<CreateGameServerProvisioningRequest, ValidatedProvisioningPlan>? planFactory = null)
    {
        var store = CreateStore(database);
        var builder = new StubPlanBuilder(planFactory ?? (request => CreateValidatedPlan(
            request.GameServerId, 8211, $"servers/{request.GameServerId}/data")));
        var engine = new ProvisioningEngine(store, CreateSteps(), NullLogger<ProvisioningEngine>.Instance);
        return new ProvisioningHarness(store, new GameServerProvisioningService(builder, store, engine));
    }

    private static IReadOnlyCollection<IProvisioningStep> CreateSteps(params IProvisioningStep[] overrides)
    {
        var byId = overrides.ToDictionary(step => step.Id, StringComparer.Ordinal);
        return ProvisioningStepIds.ExecutableFoundation
            .Select(id => byId.TryGetValue(id, out var step)
                ? step
                : new NoHostMutationProvisioningStep(id))
            .ToArray();
    }

    private static ProvisioningContext CreateContext(ManagedServerReservationResult reservation)
    {
        var plan = CreateValidatedPlan(reservation.GameServerId, 8211, $"servers/{reservation.GameServerId}/data");
        return new ProvisioningContext(reservation.ProvisioningOperationId, new PalworldGameDefinition(), plan, reservation);
    }

    private static ValidatedProvisioningPlan CreateValidatedPlan(string serverId, int port, string storagePath) =>
        new(
            new GameServerId(serverId),
            new GameId("palworld"),
            $"Server {serverId}",
            "docker",
            GameCompatibilityStatuses.Compatible,
            [],
            [new ValidatedProvisioningPort("game", "tcp", port, "public")],
            [new ValidatedProvisioningStorage("data", storagePath)],
            [],
            ProvisioningStepIds.All);

    private static ManagedServerProvisioningPlan CreatePersistencePlan(
        string serverId,
        int port = 8211,
        string? storagePath = null) =>
        new(
            serverId,
            "palworld",
            $"Server {serverId}",
            "docker",
            [new PortReservationPlan("game", "tcp", port, "public")],
            [new StorageReservationPlan("data", storagePath ?? $"servers/{serverId}/data")]);

    private static CreateGameServerProvisioningRequest CreateRequest(string serverId) =>
        new(serverId, "palworld", $"Server {serverId}");

    private static HostCapabilitySnapshot CreateCompatibleHost(
        int logicalProcessors = 4,
        string architecture = "x64") =>
        new(
            new HostOperatingSystemInfo("linux", "Linux", architecture),
            new HostCpuInfo(logicalProcessors, architecture),
            new HostMemoryInfo(HostCapabilityStatuses.Available, 32UL * 1024 * 1024 * 1024, 24UL * 1024 * 1024 * 1024),
            new HostStorageInfo(HostCapabilityStatuses.Available, string.Empty, 1000UL * 1024 * 1024 * 1024, 900UL * 1024 * 1024 * 1024),
            new HostNetworkInfo(HostCapabilityStatuses.Available, 1, true, true, true),
            [new HostRuntimeInfo("docker", "Docker", HostCapabilityStatuses.Available, true, true, "1", "linux", [])],
            new HostReadinessInfo(HostReadinessStatuses.Ready, "Ready"),
            []);

    private static GamesHudDbContext CreateInitializedDbContext(string dataRoot)
    {
        var database = CreateDbContext(dataRoot);
        new PersistenceInitializer(
            database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })),
            Options.Create(new PersistenceOptions { AutoMigrate = true }))
            .InitializeAsync().GetAwaiter().GetResult();
        return database;
    }

    private static GamesHudDbContext CreateDbContext(string dataRoot)
    {
        var layout = new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })).ResolveLayout();
        return new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(layout.DatabasePath))
            .Options);
    }

    private static ManagedServerStore CreateStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database));

    private sealed record ProvisioningHarness(ManagedServerStore Store, GameServerProvisioningService Service);

    private sealed class StubPlanBuilder(Func<CreateGameServerProvisioningRequest, ValidatedProvisioningPlan> factory)
        : IProvisioningPlanBuilder
    {
        public Task<ProvisioningPlanBuildResult> BuildAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProvisioningPlanBuildResult(factory(request), new PalworldGameDefinition(), null));
    }

    private sealed class StubHostCapabilityService(HostCapabilitySnapshot snapshot) : IHostCapabilityService
    {
        public Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class StubPortPlanner : IPortPlanner
    {
        public Task<GamePortPlan> CreatePlanAsync(GameDefinition definition, CancellationToken cancellationToken)
        {
            var items = definition.Ports.Select(port => new GamePortPlanItem(
                port.Id, port.Label, port.Purpose, port.Exposure, port.Required, port.AllowAlternative,
                new PortAvailability(port.DefaultPort, PortAvailabilityStatuses.Available, true, [], "Available"),
                new PortAllocationResult(port.DefaultPort, port.DefaultPort, false, PortAllocationStatuses.Allocated, null, "Allocated", [port.DefaultPort]))).ToArray();
            return Task.FromResult(new GamePortPlan(definition.GameId.ToString(), definition.DisplayName, PortPlanStatuses.Ready, items, "Ready"));
        }
    }

    private sealed class StubStoragePlanner : IGameStoragePlanner
    {
        public GameStoragePlan CreatePlan(GameDefinition definition, GameServerId gameServerId)
        {
            if (gameServerId.ToString().Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid id.");
            }

            var entries = definition.Storages.Select(storage => new GameStoragePlanEntry(
                storage.Id, storage.Label, storage.Purpose, StorageOwnerships.Managed,
                $"C:/internal/{gameServerId}/{storage.Id}", $"servers/{gameServerId}/{storage.Id}",
                storage.RuntimeTarget, storage.Persistent, storage.Required, storage.BackupEligible,
                storage.UserData, storage.MinimumBytes, StoragePlanStatuses.Ready)).ToArray();
            return new GameStoragePlan(gameServerId, definition.GameId.ToString(), definition.DisplayName,
                StoragePlanStatuses.Ready, "C:/internal", $"C:/internal/{gameServerId}", $"servers/{gameServerId}",
                StorageOwnerships.Managed, null, null, entries, [], [], "Ready");
        }
    }

    private sealed class FakeStep : IProvisioningStep, ICompensatingProvisioningStep
    {
        private readonly ProvisioningStepResult _result;
        private readonly Action? _compensation;
        private readonly Exception? _exception;

        public FakeStep(string id, ProvisioningStepResult? result = null, Action? compensation = null, Exception? exception = null)
        {
            Id = id;
            _result = result ?? ProvisioningStepResult.Success();
            _compensation = compensation;
            _exception = exception;
        }

        public string Id { get; }

        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            _exception is null ? Task.FromResult(_result) : Task.FromException<ProvisioningStepResult>(_exception);

        public Task CompensateAsync(ProvisioningContext context, CancellationToken cancellationToken)
        {
            _compensation?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ObservingStep(
        string id,
        Func<Task<ProvisioningStepResult>> execute) : IProvisioningStep
    {
        public string Id { get; } = id;

        public Task<ProvisioningStepResult> ExecuteAsync(
            ProvisioningContext context,
            CancellationToken cancellationToken) => execute();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-provisioning-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
