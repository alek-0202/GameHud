using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
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
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class TrustedDockerImageAcquisitionTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ImageId = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string OtherImageId = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public async Task InspectMatchesExactDigestPlatformAndLocalIdWithoutRepoTag()
    {
        var image = Image();
        var client = new FakeDockerImageClient
        {
            Inspections = new(new[] { new DockerImageInspectionSnapshot(ImageId, [image.Reference], "linux", "amd64", null) })
        };

        var result = await Adapter(client).InspectAsync(image, CancellationToken.None);

        Assert.Equal(RuntimeImageInspectionStatuses.Matches, result.Status);
        Assert.Equal(ImageId, result.LocalImageId!.Value);
        Assert.Equal(image.Reference, client.InspectedReferences.Single());
    }

    [Fact]
    public async Task DockerHubCanonicalDigestWithoutRegistryPrefixStillMatches()
    {
        var image = Image();
        var result = await Adapter(new FakeDockerImageClient
        {
            Inspections = new(new[] { new DockerImageInspectionSnapshot(ImageId,
                [$"trusted/palworld@{Digest}"], "linux", "amd64", null) })
        }).InspectAsync(image, CancellationToken.None);

        Assert.Equal(RuntimeImageInspectionStatuses.Matches, result.Status);
    }

    [Theory]
    [InlineData("digest", RuntimeImageInspectionStatuses.Mismatch)]
    [InlineData("platform", RuntimeImageInspectionStatuses.Mismatch)]
    [InlineData("malformed", RuntimeImageInspectionStatuses.Unprovable)]
    public async Task InspectFailsClosedForMismatchAndMalformedProviderState(string scenario, string expected)
    {
        var image = Image();
        var snapshot = scenario switch
        {
            "digest" => new DockerImageInspectionSnapshot(ImageId,
                [$"docker.io/trusted/palworld@{OtherDigest}"], "linux", "amd64", null),
            "platform" => new DockerImageInspectionSnapshot(ImageId, [image.Reference], "linux", "arm64", "v8"),
            _ => new DockerImageInspectionSnapshot("invalid", [image.Reference], null, null, null)
        };

        var result = await Adapter(new FakeDockerImageClient { Inspections = new(new[] { snapshot }) })
            .InspectAsync(image, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.LocalImageId);
    }

    [Fact]
    public async Task InspectDistinguishesProvenAbsenceFromProviderFailure()
    {
        var absent = await Adapter(new FakeDockerImageClient { Inspections = new(new DockerImageInspectionSnapshot?[] { null }) })
            .InspectAsync(Image(), CancellationToken.None);
        var unavailable = await Adapter(new FakeDockerImageClient { InspectException = new HttpRequestException("provider payload") })
            .InspectAsync(Image(), CancellationToken.None);

        Assert.Equal(RuntimeImageInspectionStatuses.NotFound, absent.Status);
        Assert.Equal(RuntimeImageInspectionStatuses.ProviderUnavailable, unavailable.Status);
    }

    [Fact]
    public async Task PullUsesOnlyApprovedDigestAndPlatformAndDoesNotClaimSuccess()
    {
        var client = new FakeDockerImageClient { PullResult = new(DockerImagePullStatuses.Dispatched) };

        var result = await Adapter(client).AcquireAsync(Image(), CancellationToken.None);

        Assert.Equal(RuntimeImageAcquisitionStatuses.Dispatched, result.Status);
        Assert.Equal($"docker.io/trusted/palworld@{Digest}", client.PulledReference);
        Assert.Equal("linux/amd64", client.PulledPlatform);
        Assert.DoesNotContain(":latest", client.PulledReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdapterClassifiesTimeoutAfterDispatch()
    {
        var client = new FakeDockerImageClient { PullException = new OperationCanceledException() };

        var result = await Adapter(client).AcquireAsync(Image(), CancellationToken.None);

        Assert.Equal(RuntimeImageAcquisitionStatuses.Interrupted, result.Status);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task InvalidTimeoutConfigurationFailsBeforeProviderDispatch()
    {
        var client = new FakeDockerImageClient();

        var result = await Adapter(client, new RuntimeImageAcquisitionOptions { TimeoutSeconds = 3601 })
            .AcquireAsync(Image(), CancellationToken.None);

        Assert.Equal(RuntimeImageAcquisitionStatuses.Rejected, result.Status);
        Assert.Equal(string.Empty, client.PulledReference);
    }

    [Theory]
    [InlineData("no space left on device", DockerImagePullStatuses.DiskFull)]
    [InlineData("manifest unknown", DockerImagePullStatuses.DigestUnavailable)]
    [InlineData("authentication required", DockerImagePullStatuses.AuthenticationUnsupported)]
    [InlineData("provider rejected request", DockerImagePullStatuses.Rejected)]
    public void ProgressErrorsAreSanitizedIntoClosedStatuses(string providerMessage, string expected)
    {
        var progress = new DockerImageProgressObserver();

        progress.Report(new JSONMessage { Status = "downloading" });
        progress.Report(new JSONMessage { Error = new JSONError { Message = providerMessage } });

        Assert.Equal(expected, progress.ErrorStatus);
        Assert.Equal(2, progress.ObservedMessages);
    }

    [Fact]
    public void ProgressHandlingUsesConstantMemoryAndIgnoresMalformedMessages()
    {
        var progress = new DockerImageProgressObserver();
        progress.Report(null!);
        for (var index = 0; index <= 100_000; index++) progress.Report(new JSONMessage { Status = "layer" });

        Assert.True(progress.WasBounded);
        Assert.Equal(100_000, progress.ObservedMessages);
        Assert.Null(progress.ErrorStatus);
    }

    [Fact]
    public async Task ExistingApprovedImageSkipsPullAndPersistsIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter(
            [RuntimeImageInspectionResult.Match(new LocalImageId(ImageId))]);

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
        Assert.Equal(0, adapter.AcquireCalls);
        Assert.Equal(ImageId, (await fixture.Images.LoadAsync(fixture.Owner)).VerifiedLocalImageId!.Value);
    }

    [Fact]
    public async Task ProvenAbsencePullsAndRequiresFinalInspectBeforeSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter([
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound),
            RuntimeImageInspectionResult.Match(new LocalImageId(ImageId))]);

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
        Assert.Equal(2, adapter.InspectCalls);
        Assert.Equal(1, adapter.AcquireCalls);
        Assert.All(adapter.Images, image => Assert.Equal(Digest, image.ApprovedDigest!.Value));
    }

    [Theory]
    [InlineData(RuntimeImageAcquisitionStatuses.Dispatched, RuntimeImageAcquisitionErrorCodes.OutcomeUnknown, ProvisioningFailureTypes.Unknown)]
    [InlineData(RuntimeImageAcquisitionStatuses.DiskFull, RuntimeImageAcquisitionErrorCodes.DiskFull, ProvisioningFailureTypes.Transient)]
    [InlineData(RuntimeImageAcquisitionStatuses.DigestUnavailable, RuntimeImageAcquisitionErrorCodes.DigestUnavailable, ProvisioningFailureTypes.Permanent)]
    [InlineData(RuntimeImageAcquisitionStatuses.AuthenticationUnsupported, RuntimeImageAcquisitionErrorCodes.AuthenticationUnsupported, ProvisioningFailureTypes.Permanent)]
    public async Task FinalAbsenceClassifiesPullResultSafely(string acquisitionStatus, string code, string failureType)
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter([
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound),
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)],
            new(acquisitionStatus));

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(failureType, result.FailureType);
        Assert.Null((await fixture.Images.LoadAsync(fixture.Owner)).VerifiedLocalImageId);
    }

    [Fact]
    public async Task FinalInspectFailureAfterDispatchIsUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter([
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound),
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.ProviderUnavailable)]);

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningFailureTypes.Unknown, result.FailureType);
        Assert.Equal(RuntimeImageAcquisitionErrorCodes.ProviderUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task CancellationBeforePullDispatchUsesSafeCancellationSignal()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ProvisioningCancelledBeforeMutationException>(() =>
            fixture.Step(new FakeAcquisitionAdapter([])).ExecuteAsync(fixture.Context, cancellation.Token));
    }

    [Fact]
    public async Task InterruptionAfterDispatchIsUnknownAndKeepsIdentityUnverified()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter([
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound),
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)],
            new(RuntimeImageAcquisitionStatuses.Interrupted));

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningFailureTypes.Unknown, result.FailureType);
        Assert.Equal(RuntimeImageAcquisitionErrorCodes.Interrupted, result.ErrorCode);
    }

    [Fact]
    public async Task TimeoutAfterDispatchIsUnknownAndRequiresReconciliation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adapter = new FakeAcquisitionAdapter([
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound),
            RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)],
            new(RuntimeImageAcquisitionStatuses.Interrupted, TimedOut: true));

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningFailureTypes.Unknown, result.FailureType);
        Assert.Equal(RuntimeImageAcquisitionErrorCodes.Timeout, result.ErrorCode);
    }

    [Fact]
    public async Task ConflictingPersistedLocalIdentityFailsClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Images.RecordVerifiedAsync(fixture.Owner, Image(), new LocalImageId(ImageId), 1);
        var adapter = new FakeAcquisitionAdapter([RuntimeImageInspectionResult.Match(new LocalImageId(OtherImageId))]);

        var result = await fixture.Step(adapter).ExecuteAsync(fixture.Context, CancellationToken.None);

        Assert.Equal(ProvisioningFailureTypes.Unknown, result.FailureType);
        Assert.Equal(ImageId, (await fixture.Images.LoadAsync(fixture.Owner)).VerifiedLocalImageId!.Value);
    }

    [Fact]
    public async Task ReconciliationPersistsCrashWindowIdentityBeforeApplyingSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failed = await fixture.Operations.ApplyCheckpointAsync(new(fixture.OperationId, 1,
            ProvisioningOperationStatuses.Failed, ProvisioningStepIds.AcquireImage,
            ProvisioningStepIds.AcquireImage, ProvisioningStepStatuses.Failed,
            ProvisioningFailureTypes.Unknown, "interrupted", "Acquisition was interrupted.", KeepActiveSlot: true));
        var reconciler = new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.Match(new LocalImageId(ImageId))]));
        var service = new ProvisioningReconciliationService(fixture.Operations,
            new EfCorePersistenceTransactionBoundary(fixture.Database), [reconciler]);

        var applied = await service.ApplyAsync(fixture.OperationId, ProvisioningStepIds.AcquireImage, failed.Version);

        Assert.Equal(ProvisioningStepStatuses.Succeeded,
            applied.Steps.Single(step => step.StepId == ProvisioningStepIds.AcquireImage).Status);
        Assert.Equal(ImageId, (await fixture.Images.LoadAsync(fixture.Owner)).VerifiedLocalImageId!.Value);
        Assert.Equal(1, await fixture.Database.ProvisioningReconciliations.CountAsync());
    }

    [Fact]
    public async Task ReconciliationTreatsAbsentAsRetryableOnlyWithoutPersistedIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var absent = new FakeAcquisitionAdapter([RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)]);
        var reconciler = new AcquireImageReconciler(fixture.Servers, fixture.Images, absent);
        var operation = (await fixture.Operations.GetAsync(fixture.OperationId))!;
        var step = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);

        var result = await reconciler.InspectAsync(operation, step, CancellationToken.None);

        Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent, result.Outcome);
    }

    [Fact]
    public async Task AppliedAbsentReconciliationAuthorizesOnlyTheNextAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reconciler = new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)]));
        var service = new ProvisioningReconciliationService(fixture.Operations,
            new EfCorePersistenceTransactionBoundary(fixture.Database), [reconciler]);

        var applied = await service.ApplyAsync(fixture.OperationId, ProvisioningStepIds.AcquireImage, 1);
        var step = applied.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);

        Assert.Equal(ProvisioningStepStatuses.Failed, step.Status);
        Assert.Equal(2, step.ReconciledRetryAttempt);
        Assert.True(applied.IsActive);
    }

    [Fact]
    public async Task AmbiguousReconciliationNeverAuthorizesRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reconciler = new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.ProviderUnavailable)]));
        var service = new ProvisioningReconciliationService(fixture.Operations,
            new EfCorePersistenceTransactionBoundary(fixture.Database), [reconciler]);

        var applied = await service.ApplyAsync(fixture.OperationId, ProvisioningStepIds.AcquireImage, 1);
        var step = applied.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);

        Assert.Equal(ProvisioningFailureTypes.Unknown, step.FailureType);
        Assert.Null(step.ReconciledRetryAttempt);
        Assert.True(applied.IsActive);
    }

    [Fact]
    public async Task ReconciliationIsAmbiguousWhenPersistedIdentityDisappearedOrConflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Images.RecordVerifiedAsync(fixture.Owner, Image(), new LocalImageId(ImageId), 1);
        var operation = (await fixture.Operations.GetAsync(fixture.OperationId))!;
        var step = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);

        var absent = await new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)]))
            .InspectAsync(operation, step, CancellationToken.None);
        var conflict = await new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.Match(new LocalImageId(OtherImageId))]))
            .InspectAsync(operation, step, CancellationToken.None);

        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, absent.Outcome);
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, conflict.Outcome);
    }

    [Fact]
    public async Task ReconciliationAcceptsSameIdentityPersistedBeforeStepCheckpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Images.RecordVerifiedAsync(fixture.Owner, Image(), new LocalImageId(ImageId), 1);
        var operation = (await fixture.Operations.GetAsync(fixture.OperationId))!;
        var step = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);

        var result = await new AcquireImageReconciler(fixture.Servers, fixture.Images,
            new FakeAcquisitionAdapter([RuntimeImageInspectionResult.Match(new LocalImageId(ImageId))]))
            .InspectAsync(operation, step, CancellationToken.None);

        Assert.Equal(ProvisioningReconciliationOutcomes.EffectExists, result.Outcome);
        Assert.Equal(ImageId, (await fixture.Images.LoadAsync(fixture.Owner)).VerifiedLocalImageId!.Value);
    }

    [Fact]
    public async Task SeparateOperationsMayVerifyTheSameSharedImageIdentity()
    {
        using var root = TemporaryDirectory.Create();
        var path = Path.Combine(root.Path, "system", "gameshud.db");
        await using var database = new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options);
        await new PersistenceInitializer(database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = root.Path })),
            Options.Create(new PersistenceOptions { AutoMigrate = true })).InitializeAsync();
        var transaction = new EfCorePersistenceTransactionBoundary(database);
        var servers = new ManagedServerStore(database, transaction);
        var first = await servers.ReserveProvisioningPlanAsync(PlanFor("server-one", 8211));
        var second = await servers.ReserveProvisioningPlanAsync(PlanFor("server-two", 8212));
        await StartAcquisitionAsync(database, first.ProvisioningOperationId);
        await StartAcquisitionAsync(database, second.ProvisioningOperationId);
        var images = new RuntimeImageIntentStore(database, transaction);

        await images.RecordVerifiedAsync(new(first.ProvisioningOperationId, "server-one", "palworld", "docker"),
            Image(), new LocalImageId(ImageId), 1);
        await images.RecordVerifiedAsync(new(second.ProvisioningOperationId, "server-two", "palworld", "docker"),
            Image(), new LocalImageId(ImageId), 1);

        Assert.Equal(2, await database.RuntimeImageIntents.CountAsync(item => item.VerifiedLocalImageId == ImageId));
    }

    [Fact]
    public void AcquisitionBoundaryDoesNotExposeDockerSdkModelsOrDestructiveOperations()
    {
        var methods = typeof(IRuntimeImageAcquisitionAdapter).GetMethods();

        Assert.Equal(["AcquireAsync", "InspectAsync"], methods.Select(method => method.Name).Order().ToArray());
        Assert.DoesNotContain(methods.SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType),
            type => type.FullName?.StartsWith("Docker.DotNet", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(methods.Select(method => method.ReturnType),
            type => type.FullName?.StartsWith("Docker.DotNet", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProductionPalworldAndDefaultPipelineRemainGated()
    {
        var palworld = new PalworldGameDefinition();

        Assert.Equal("gh09-v1", ProvisioningPipelines.DefaultVersion);
        Assert.Equal(9, ProvisioningPipelines.Legacy.Steps.Count);
        Assert.Equal(10, ProvisioningPipelines.ImageAcquisition.Steps.Count);
        Assert.False(palworld.RuntimeImages.Single().IsPinned);
        Assert.Equal("latest", palworld.RuntimeImages.Single().Tag);
    }

    private static DockerRuntimeImageAcquisitionAdapter Adapter(FakeDockerImageClient client,
        RuntimeImageAcquisitionOptions? options = null) => new(client, Options.Create(options ?? new()));

    private static TrustedRuntimeImage Image() => new("docker", "docker.io", "trusted/palworld",
        new ApprovedImageDigest(Digest), new RuntimeImagePlatform("linux", "amd64"), "catalog-v1");

    private static ManagedServerProvisioningPlan PlanFor(string serverId, int port) => new(serverId, "palworld", "Server", "docker",
        [new("game", "udp", port, "public")], [new("data", $"servers/{serverId}/data")],
        PipelineVersion: ProvisioningPipelines.ImageAcquisitionVersion,
        Steps: ProvisioningPipelines.ImageAcquisition.Steps.Select(step => new ProvisioningStepPlan(step.Id,
            step.Sequence, step.RetryClassification, step.SideEffectClassification, step.MaxAttempts, step.Sequence <= 3)).ToArray(),
        RuntimeImage: Image());

    private static async Task StartAcquisitionAsync(GamesHudDbContext database, string operationId)
    {
        var operation = await database.ProvisioningOperations.Include(item => item.Steps).SingleAsync(item => item.Id == operationId);
        operation.Status = ProvisioningOperationStatuses.Running;
        operation.CurrentStep = ProvisioningStepIds.AcquireImage;
        foreach (var step in operation.Steps.Where(item => item.Sequence is 4 or 5))
        {
            step.Status = ProvisioningStepStatuses.Succeeded;
            step.Attempt = 1;
        }
        var acquisition = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);
        acquisition.Status = ProvisioningStepStatuses.Running;
        acquisition.Attempt = 1;
        await database.SaveChangesAsync();
    }

    private sealed class FakeDockerImageClient : IDockerImageAcquisitionClient
    {
        public Queue<DockerImageInspectionSnapshot?> Inspections { get; init; } = new();
        public Exception? InspectException { get; init; }
        public DockerImagePullResult PullResult { get; init; } = new(DockerImagePullStatuses.Dispatched);
        public Exception? PullException { get; init; }
        public List<string> InspectedReferences { get; } = [];
        public string PulledReference { get; private set; } = string.Empty;
        public string PulledPlatform { get; private set; } = string.Empty;

        public Task<DockerImageInspectionSnapshot?> InspectAsync(string reference, CancellationToken cancellationToken)
        {
            InspectedReferences.Add(reference);
            return InspectException is null ? Task.FromResult(Inspections.Dequeue())
                : Task.FromException<DockerImageInspectionSnapshot?>(InspectException);
        }

        public Task<DockerImagePullResult> PullAsync(string reference, string platform, CancellationToken cancellationToken)
        {
            PulledReference = reference;
            PulledPlatform = platform;
            return PullException is null ? Task.FromResult(PullResult) : Task.FromException<DockerImagePullResult>(PullException);
        }
    }

    private sealed class FakeAcquisitionAdapter(Queue<RuntimeImageInspectionResult> inspections,
        RuntimeImageAcquisitionResult? acquisition = null) : IRuntimeImageAcquisitionAdapter
    {
        public FakeAcquisitionAdapter(IEnumerable<RuntimeImageInspectionResult> inspections,
            RuntimeImageAcquisitionResult? acquisition = null) : this(new Queue<RuntimeImageInspectionResult>(inspections), acquisition) { }
        public int InspectCalls { get; private set; }
        public int AcquireCalls { get; private set; }
        public List<TrustedRuntimeImage> Images { get; } = [];

        public Task<RuntimeImageInspectionResult> InspectAsync(TrustedRuntimeImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            Images.Add(image);
            return Task.FromResult(inspections.Dequeue());
        }

        public Task<RuntimeImageAcquisitionResult> AcquireAsync(TrustedRuntimeImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCalls++;
            Images.Add(image);
            return Task.FromResult(acquisition ?? new(RuntimeImageAcquisitionStatuses.Dispatched));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory root;
        private Fixture(TemporaryDirectory root, GamesHudDbContext database, ManagedServerStore servers,
            RuntimeImageIntentStore images, ProvisioningOperationStore operations, ManagedServerReservationResult reservation)
        {
            this.root = root; Database = database; Servers = servers; Images = images; Operations = operations;
            OperationId = reservation.ProvisioningOperationId;
            Owner = new(OperationId, "server-one", "palworld", "docker");
            var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
                "docker", "compatible", [], [], [], [], ProvisioningPipelines.ImageAcquisition.Steps.Select(item => item.Id).ToArray());
            Context = new(OperationId, Definition(), plan, reservation, userRequestedCancellation: false);
        }

        public GamesHudDbContext Database { get; }
        public ManagedServerStore Servers { get; }
        public RuntimeImageIntentStore Images { get; }
        public ProvisioningOperationStore Operations { get; }
        public string OperationId { get; }
        public RuntimeImageOwner Owner { get; }
        public ProvisioningContext Context { get; }

        public AcquireImageProvisioningStep Step(IRuntimeImageAcquisitionAdapter adapter) =>
            new(Images, adapter, Options.Create(new RuntimeImageAcquisitionOptions()));

        public static async Task<Fixture> CreateAsync()
        {
            var root = TemporaryDirectory.Create();
            var path = Path.Combine(root.Path, "system", "gameshud.db");
            var database = new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options);
            await new PersistenceInitializer(database,
                new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = root.Path })),
                Options.Create(new PersistenceOptions { AutoMigrate = true })).InitializeAsync();
            var transaction = new EfCorePersistenceTransactionBoundary(database);
            var servers = new ManagedServerStore(database, transaction);
            var reservation = await servers.ReserveProvisioningPlanAsync(Plan());
            var operation = await database.ProvisioningOperations.Include(item => item.Steps)
                .SingleAsync(item => item.Id == reservation.ProvisioningOperationId);
            operation.Status = ProvisioningOperationStatuses.Running;
            operation.CurrentStep = ProvisioningStepIds.AcquireImage;
            foreach (var step in operation.Steps.Where(item => item.Sequence is 4 or 5))
            {
                step.Status = ProvisioningStepStatuses.Succeeded;
                step.Attempt = 1;
            }
            var acquisition = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);
            acquisition.Status = ProvisioningStepStatuses.Running;
            acquisition.Attempt = 1;
            await database.SaveChangesAsync();
            return new(root, database, servers, new RuntimeImageIntentStore(database, transaction),
                new ProvisioningOperationStore(database, transaction, new ProvisioningStateMachine()), reservation);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            root.Dispose();
        }

        private static ManagedServerProvisioningPlan Plan() => new("server-one", "palworld", "Server", "docker",
            [new("game", "udp", 8211, "public")], [new("data", "servers/server-one/data")],
            PipelineVersion: ProvisioningPipelines.ImageAcquisitionVersion,
            Steps: ProvisioningPipelines.ImageAcquisition.Steps.Select(step => new ProvisioningStepPlan(step.Id,
                step.Sequence, step.RetryClassification, step.SideEffectClassification, step.MaxAttempts, step.Sequence <= 3)).ToArray(),
            RuntimeImage: Image());

        private static GameDefinition Definition() => new(new GameId("palworld"), "Palworld", "Test",
            new GameDefinitionBranding("palworld"), ["docker"], ["test"], ports:
            [new GamePortDefinition("game", "Game", 8211, PortProtocols.Udp, true, true, PortExposures.Public, "Test", "test")],
            storages: [new GameStorageDefinition("data", "Data", StoragePurposes.GameData, "/palworld", true, true, true, true, null, "test")],
            runtimeImages: [Image()]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshud-gh15-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
