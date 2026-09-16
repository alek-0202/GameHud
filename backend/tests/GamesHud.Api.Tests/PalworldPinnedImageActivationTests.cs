using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldPinnedImageActivationTests
{
    private const string ApprovedDigest = "sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9";
    private const string IndexDigest = "sha256:be3ad49e373045a7b60478fd8b7f7411c1e293713dfa4733e563e798f276d688";

    [Fact]
    public void CatalogUsesApprovedPlatformManifestAndKeepsLegacySelectorSeparate()
    {
        var definition = new PalworldGameDefinition();
        var image = Assert.Single(definition.RuntimeImages);
        var legacy = Assert.Single(definition.LegacyRuntimeImages);

        Assert.True(image.IsPinned);
        Assert.Equal("docker.io", image.Registry);
        Assert.Equal("thijsvanloef/palworld-server-docker", image.Repository);
        Assert.Equal(ApprovedDigest, image.ApprovedDigest!.Value);
        Assert.NotEqual(IndexDigest, image.ApprovedDigest.Value);
        Assert.Equal("linux", image.Platform!.OperatingSystem);
        Assert.Equal("amd64", image.Platform.Architecture);
        Assert.Null(image.Platform.Variant);
        Assert.Equal("gameshud-catalog/palworld/v2.7.3", image.Source);
        Assert.Equal($"docker.io/thijsvanloef/palworld-server-docker@{ApprovedDigest}", image.Reference);
        Assert.DoesNotContain("latest", image.Reference, StringComparison.OrdinalIgnoreCase);
        Assert.False(legacy.IsPinned);
        Assert.Equal("latest", legacy.Tag);
    }

    [Fact]
    public void ManagedSelectionActivatesOnlyEligiblePinnedCatalogEntries()
    {
        var selected = ProvisioningPipelines.SelectForManaged(new PalworldGameDefinition(), "docker");
        var legacyDefinition = Definition(new TrustedRuntimeImage("docker", "trusted/game", "latest", "test"));
        var unsupportedPlatform = Definition(new TrustedRuntimeImage("docker", "docker.io", "trusted/game",
            new ApprovedImageDigest(ApprovedDigest), new RuntimeImagePlatform("linux", "arm64"), "test"));

        Assert.Equal(ProvisioningPipelines.ImageAcquisitionVersion, selected.Pipeline.Version);
        Assert.Equal(ApprovedDigest, selected.RuntimeImage!.ApprovedDigest!.Value);
        Assert.Equal(10, selected.Pipeline.Steps.Count);
        Assert.True(selected.Pipeline.Steps.Single(step => step.Id == ProvisioningStepIds.AcquireImage).Sequence
            < selected.Pipeline.Steps.Single(step => step.Id == ProvisioningStepIds.CreateRuntime).Sequence);
        Assert.Equal("gh09-v1", ProvisioningPipelines.SelectForManaged(legacyDefinition, "docker").Pipeline.Version);
        Assert.Null(ProvisioningPipelines.SelectForManaged(legacyDefinition, "docker").RuntimeImage);
        Assert.Equal("gh09-v1", ProvisioningPipelines.SelectForManaged(unsupportedPlatform, "docker").Pipeline.Version);
        Assert.Equal(9, ProvisioningPipelines.Legacy.Steps.Count);
    }

    [Fact]
    public async Task NewManagedPalworldPersistsExactPendingV2IntentAndReloadsIt()
    {
        using var root = TemporaryDirectory.Create();
        string operationId;
        await using (var database = CreateInitialized(root.Path))
        {
            var transaction = new EfCorePersistenceTransactionBoundary(database);
            var servers = new ManagedServerStore(database, transaction);
            var operations = new ProvisioningOperationStore(database, transaction, new ProvisioningStateMachine());
            var engine = new CapturingEngine();
            var service = new GameServerProvisioningService(new StubPlanBuilder(), servers, operations, engine);

            var result = await service.StartProvisioningAsync(
                new CreateGameServerProvisioningRequest("server-one", "palworld", "Server"), CancellationToken.None);

            Assert.True(result.Succeeded);
            operationId = result.OperationId!;
            var operation = await database.ProvisioningOperations.Include(item => item.Steps)
                .SingleAsync(item => item.Id == operationId);
            var intent = await database.RuntimeImageIntents.SingleAsync(item => item.OperationId == operationId);
            Assert.Equal(ProvisioningPipelines.ImageAcquisitionVersion, operation.PipelineVersion);
            Assert.Equal(10, operation.Steps.Count);
            Assert.Equal("docker.io", intent.Registry);
            Assert.Equal("thijsvanloef/palworld-server-docker", intent.Repository);
            Assert.Equal(ApprovedDigest, intent.ApprovedDigest);
            Assert.Equal("linux", intent.PlatformOs);
            Assert.Equal("amd64", intent.PlatformArchitecture);
            Assert.Null(intent.PlatformVariant);
            Assert.Equal("gameshud-catalog/palworld/v2.7.3", intent.ApprovalSource);
            Assert.Null(intent.VerifiedLocalImageId);
            Assert.Equal("pending", intent.VerificationState);
            Assert.Equal(operationId, engine.Context!.OperationId);
        }

        await using var reloaded = CreateContext(root.Path);
        var transactionReloaded = new EfCorePersistenceTransactionBoundary(reloaded);
        var snapshot = await new RuntimeImageIntentStore(reloaded, transactionReloaded).LoadAsync(
            new RuntimeImageOwner(operationId, "server-one", "palworld", "docker"));
        var unrelatedCatalogChange = new ApprovedImageDigest(
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.Equal(ApprovedDigest, snapshot.Image.ApprovedDigest!.Value);
        Assert.NotEqual(unrelatedCatalogChange, snapshot.Image.ApprovedDigest);
        Assert.Equal($"docker.io/thijsvanloef/palworld-server-docker@{ApprovedDigest}", snapshot.Image.Reference);
        Assert.Null(snapshot.VerifiedLocalImageId);
    }

    [Fact]
    public async Task HistoricalV1ReservationKeepsNineStepsAndNeedsNoImageIntent()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var store = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database));

        var reservation = await store.ReserveProvisioningPlanAsync(new ManagedServerProvisioningPlan(
            "legacy-server", "palworld", "Legacy", "docker",
            [new PortReservationPlan("game", "udp", 8211, "public")],
            [new StorageReservationPlan("data", "servers/legacy-server/data")]));

        var operation = await database.ProvisioningOperations.Include(item => item.Steps)
            .SingleAsync(item => item.Id == reservation.ProvisioningOperationId);
        Assert.Equal("gh09-v1", operation.PipelineVersion);
        Assert.Equal(9, operation.Steps.Count);
        Assert.DoesNotContain(operation.Steps, step => step.StepId == ProvisioningStepIds.AcquireImage);
        Assert.Empty(await database.RuntimeImageIntents.ToArrayAsync());
    }

    private static GameDefinition Definition(TrustedRuntimeImage image) => new(
        new GameId("test"), "Test", "Test", new GameDefinitionBranding("test"), ["docker"], ["test"],
        runtimeImages: [image]);

    private static GamesHudDbContext CreateInitialized(string dataRoot)
    {
        var database = CreateContext(dataRoot);
        new PersistenceInitializer(database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })),
            Options.Create(new PersistenceOptions { AutoMigrate = true }))
            .InitializeAsync().GetAwaiter().GetResult();
        return database;
    }

    private static GamesHudDbContext CreateContext(string dataRoot)
    {
        var layout = new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })).ResolveLayout();
        return new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(layout.DatabasePath)).Options);
    }

    private sealed class StubPlanBuilder : IProvisioningPlanBuilder
    {
        public Task<ProvisioningPlanBuildResult> BuildAsync(CreateGameServerProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            var plan = new ValidatedProvisioningPlan(
                new GameServerId(request.GameServerId), new GameId("palworld"), request.DisplayName, "docker",
                "compatible", [], [new("game", "udp", 8211, "public")],
                [new("data", "servers/server-one/data")], [], ProvisioningStepIds.All);
            return Task.FromResult(new ProvisioningPlanBuildResult(plan, new PalworldGameDefinition(), null));
        }
    }

    private sealed class CapturingEngine : IProvisioningEngine
    {
        public ProvisioningContext? Context { get; private set; }

        public Task<ProvisioningExecutionResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(new ProvisioningExecutionResult(
                true, context.OperationId, ProvisioningOperationStatuses.Pending, null));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-gh15a-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
