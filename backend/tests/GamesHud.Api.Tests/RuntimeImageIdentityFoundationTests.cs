using Docker.DotNet.Models;
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

public sealed class RuntimeImageIdentityFoundationTests
{
    private const string DigestA = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ImageIdA = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string ImageIdB = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void PinnedContractValidatesAndNormalizesDistinctIdentityParts()
    {
        var image = Image(DigestA, "Registry.Example:5000", "Team/Game");

        Assert.Equal("registry.example:5000", image.Registry);
        Assert.Equal("team/game", image.Repository);
        Assert.Equal(DigestA, image.ApprovedDigest!.Value);
        Assert.Equal("linux", image.Platform!.OperatingSystem);
        Assert.Equal("amd64", image.Platform.Architecture);
        Assert.Equal($"registry.example:5000/team/game@{DigestA}", image.Reference);
        Assert.True(image.IsPinned);
    }

    [Theory]
    [InlineData("sha256:abc")]
    [InlineData("sha512:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void PinnedContractRejectsInvalidDigest(string digest) =>
        Assert.Throws<ArgumentException>(() => new ApprovedImageDigest(digest));

    [Theory]
    [InlineData("https://registry.example", "team/game")]
    [InlineData("registry.example", "Team//Game")]
    [InlineData("registry.example:70000", "team/game")]
    public void PinnedContractRejectsInvalidRegistryOrRepository(string registry, string repository) =>
        Assert.Throws<ArgumentException>(() => Image(DigestA, registry, repository));

    [Fact]
    public void LegacyTagPreservesSelectorCaseWhileRepositoryIsCanonical()
    {
        var legacy = new TrustedRuntimeImage("docker", "Example/Game", "Release-A", "game_definition");

        Assert.Equal("example/game", legacy.Repository);
        Assert.Equal("Release-A", legacy.Tag);
        Assert.False(legacy.IsPinned);
    }

    [Fact]
    public void ProvisioningRequestHasNoRuntimeImageInput()
    {
        var properties = typeof(CreateGameServerProvisioningRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["GameServerId", "GameId", "DisplayName"], properties);
    }

    [Fact]
    public void PipelineRegistryPreservesV1AndGatesV2BehindNonDefaultVersion()
    {
        var v1 = ProvisioningPipelines.Legacy;
        var v2 = ProvisioningPipelines.ImageAcquisition;

        Assert.Equal("gh09-v1", ProvisioningPipelines.DefaultVersion);
        Assert.Equal(9, v1.Steps.Count);
        Assert.DoesNotContain(v1.Steps, step => step.Id == ProvisioningStepIds.AcquireImage);
        Assert.Equal(10, v2.Steps.Count);
        Assert.Equal(6, v2.Steps.Single(step => step.Id == ProvisioningStepIds.AcquireImage).Sequence);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, v2.Steps.Single(step => step.Sequence == 5).Id);
        Assert.Equal(ProvisioningStepIds.CreateRuntime, v2.Steps.Single(step => step.Sequence == 7).Id);
        Assert.Equal(ProvisioningRetryClassifications.RequiresInspection,
            v2.Steps.Single(step => step.Id == ProvisioningStepIds.AcquireImage).RetryClassification);
        Assert.Equal(3, v2.Steps.Single(step => step.Id == ProvisioningStepIds.AcquireImage).MaxAttempts);
    }

    [Fact]
    public async Task V2ReservationCreatesIntentTransactionallyAndReloadsAcrossScope()
    {
        using var root = TemporaryDirectory.Create();
        string operationId;
        await using (var database = CreateInitialized(root.Path))
        {
            var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
            operationId = reservation.ProvisioningOperationId;
            Assert.Equal(1, await database.RuntimeImageIntents.CountAsync());
            Assert.Equal(1, await database.ProvisioningOperations.CountAsync());
            Assert.Equal(10, await database.ProvisioningSteps.CountAsync());
        }

        await using var reloaded = CreateContext(root.Path);
        var store = ImageStore(reloaded);
        var snapshot = await store.LoadAsync(Owner(operationId));
        Assert.Equal(DigestA, snapshot.Image.ApprovedDigest!.Value);
        Assert.Null(snapshot.VerifiedLocalImageId);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public async Task V2OperationCannotBeSavedWithoutTransactionalImageIntent()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        database.ManagedGameServers.Add(new ManagedGameServerRecord
        {
            Id = "server-one", GameId = "palworld", DisplayName = "Server", InstallationType = ManagedInstallationTypes.Managed,
            RuntimeType = "docker", LifecycleState = ManagedGameServerLifecycleStates.PendingProvisioning
        });
        database.ProvisioningOperations.Add(new ProvisioningOperationRecord
        {
            Id = Guid.NewGuid().ToString("N"), GameServerId = "server-one", Type = ProvisioningOperationTypes.Provision,
            Status = ProvisioningOperationStatuses.Pending, ActiveSlot = ProvisioningOperationActiveSlots.Active,
            CurrentStep = ProvisioningStepIds.ReserveResources, PipelineVersion = ProvisioningPipelines.ImageAcquisitionVersion, Version = 1
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovedDigestAndPlatformAreImmutable(bool changeDigest)
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var intent = await database.RuntimeImageIntents.SingleAsync();
        if (changeDigest) intent.ApprovedDigest = DigestB;
        else intent.PlatformArchitecture = "arm64";

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task MigrationAppliesToAnEmptySqliteDatabase()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);

        Assert.Contains("20260915122024_AddDurableRuntimeImageIdentity", await database.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await database.RuntimeImageIntents.CountAsync());
        Assert.Equal(0, await database.ProvisioningReconciliations.CountAsync());
    }

    [Fact]
    public async Task VerifiedIdentityIsDurableIdempotentAndCannotBeReplaced()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        await StartAcquisitionAsync(database, reservation.ProvisioningOperationId);
        var store = ImageStore(database);

        var verified = await store.RecordVerifiedAsync(Owner(reservation.ProvisioningOperationId), Image(DigestA),
            new LocalImageId(ImageIdA), 1);
        var same = await store.RecordVerifiedAsync(Owner(reservation.ProvisioningOperationId), Image(DigestA),
            new LocalImageId(ImageIdA), 1);

        Assert.Equal(ImageIdA, verified.VerifiedLocalImageId!.Value);
        Assert.Equal(2, verified.Version);
        Assert.Equal(verified, same);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordVerifiedAsync(
            Owner(reservation.ProvisioningOperationId), Image(DigestA), new LocalImageId(ImageIdB), 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(
            Owner(reservation.ProvisioningOperationId) with { GameServerId = "other" }));
    }

    [Fact]
    public async Task HistoricalRuntimeImageComesFromDurableIntentAfterCatalogChanges()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        await StartAcquisitionAsync(database, reservation.ProvisioningOperationId);
        var images = ImageStore(database);
        await images.RecordVerifiedAsync(Owner(reservation.ProvisioningOperationId), Image(DigestA), new LocalImageId(ImageIdA), 1);
        var changedDefinition = Definition(Image(DigestB));
        var operations = OperationStore(database);
        var builder = new RuntimeSpecificationBuilder(CreateStore(database),
            new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = root.Path })),
            new GameDefinitionRegistry([changedDefinition]), operations, images);

        var reconstructed = await builder.BuildForReconciliationAsync(reservation.ProvisioningOperationId,
            new GameServerId("server-one"), CancellationToken.None);

        Assert.NotNull(reconstructed.Specification);
        Assert.Equal(DigestA, reconstructed.Specification!.Image.ApprovedDigest!.Value);
        Assert.Equal(ImageIdA, reconstructed.Specification.VerifiedImage!.LocalImageId.Value);
    }

    [Fact]
    public async Task V2RuntimeSpecificationFailsClosedWithoutVerifiedIdentity()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var builder = new RuntimeSpecificationBuilder(CreateStore(database),
            new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = root.Path })),
            new GameDefinitionRegistry([Definition(Image(DigestB))]), OperationStore(database), ImageStore(database));

        var reconstructed = await builder.BuildForReconciliationAsync(reservation.ProvisioningOperationId,
            new GameServerId("server-one"), CancellationToken.None);

        Assert.Null(reconstructed.Specification);
    }

    [Fact]
    public async Task AcquireImageCannotCheckpointSuccessBeforeDurableVerification()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        await StartAcquisitionAsync(database, reservation.ProvisioningOperationId);
        var operations = OperationStore(database);

        await Assert.ThrowsAsync<ProvisioningTransitionException>(() => operations.ApplyCheckpointAsync(new(
            reservation.ProvisioningOperationId, 1, ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.AcquireImage, ProvisioningStepIds.AcquireImage, ProvisioningStepStatuses.Succeeded)));
    }

    [Fact]
    public async Task VerifiedIdentityUsesOptimisticConcurrencyBeforeFirstConfirmation()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        await StartAcquisitionAsync(database, reservation.ProvisioningOperationId);

        await Assert.ThrowsAsync<ProvisioningConcurrencyException>(() => ImageStore(database).RecordVerifiedAsync(
            Owner(reservation.ProvisioningOperationId), Image(DigestA), new LocalImageId(ImageIdA), 9));
    }

    [Fact]
    public async Task V2DockerSpecificationUsesVerifiedIdAndRejectsContainerImageMismatch()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        await StartAcquisitionAsync(database, reservation.ProvisioningOperationId);
        var images = ImageStore(database);
        await images.RecordVerifiedAsync(Owner(reservation.ProvisioningOperationId), Image(DigestA), new LocalImageId(ImageIdA), 1);
        var builder = new RuntimeSpecificationBuilder(CreateStore(database),
            new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = root.Path })),
            new GameDefinitionRegistry([Definition(Image(DigestB))]), OperationStore(database), images);
        var built = await builder.BuildForReconciliationAsync(reservation.ProvisioningOperationId,
            new GameServerId("server-one"), CancellationToken.None);
        var validated = new RuntimeMutationPolicy().Validate(built.Specification!, built.Definition!, root.Path);
        Assert.True(validated.Allowed);
        var expected = DockerCreateContainerMapper.Map(new RuntimeMutationExecutionContext(validated.Specification!,
            RuntimeMutationKind.CreateRuntime, ProvisioningStepIds.CreateRuntime, 1));
        var actual = new ContainerInspectResponse
        {
            Image = ImageIdB,
            Config = new Config { Image = expected.Image, Env = expected.Env },
            HostConfig = expected.HostConfig
        };

        Assert.Equal(ImageIdA, expected.Image);
        Assert.False(DockerGameRuntimeAdapter.CriticalConfigurationMatches(actual, expected));
    }

    [Theory]
    [InlineData(ProvisioningReconciliationOutcomes.EffectExists, ProvisioningStepStatuses.Succeeded, null, true)]
    [InlineData(ProvisioningReconciliationOutcomes.EffectAbsent, ProvisioningStepStatuses.Failed, 2, true)]
    [InlineData(ProvisioningReconciliationOutcomes.Ambiguous, ProvisioningStepStatuses.Failed, null, true)]
    public async Task ReconciliationAppliesDurableAuditableState(string outcome, string status,
        int? retryAttempt, bool active)
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var operations = OperationStore(database);
        var running = await operations.ApplyCheckpointAsync(new(reservation.ProvisioningOperationId, 1,
            ProvisioningOperationStatuses.Running, ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage, ProvisioningStepStatuses.Running));
        var service = new ProvisioningReconciliationService(operations,
            new EfCorePersistenceTransactionBoundary(database), [new FakeReconciler(ProvisioningStepIds.PrepareStorage, outcome)]);

        var applied = await service.ApplyAsync(reservation.ProvisioningOperationId,
            ProvisioningStepIds.PrepareStorage, running.Version);
        var step = applied.Steps.Single(item => item.StepId == ProvisioningStepIds.PrepareStorage);

        Assert.Equal(status, step.Status);
        Assert.Equal(retryAttempt, step.ReconciledRetryAttempt);
        Assert.Equal(active, applied.IsActive);
        Assert.Equal(1, await database.ProvisioningReconciliations.CountAsync());
        if (outcome == ProvisioningReconciliationOutcomes.EffectAbsent)
        {
            var retried = await operations.ApplyCheckpointAsync(new(applied.OperationId, applied.Version,
                ProvisioningOperationStatuses.Running, step.StepId, step.StepId, ProvisioningStepStatuses.Running,
                ExplicitRetry: true));
            Assert.Equal(2, retried.Steps.Single(item => item.StepId == step.StepId).Attempt);
            Assert.Null(retried.Steps.Single(item => item.StepId == step.StepId).ReconciledRetryAttempt);
        }
    }

    [Fact]
    public async Task ReconciliationRejectsRetryWhenAttemptsAreExhausted()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var operations = OperationStore(database);
        var stepId = ProvisioningStepIds.PrepareStorage;
        var running = await operations.ApplyCheckpointAsync(new(reservation.ProvisioningOperationId, 1,
            ProvisioningOperationStatuses.Running, stepId, stepId, ProvisioningStepStatuses.Running));
        var service = new ProvisioningReconciliationService(operations, new EfCorePersistenceTransactionBoundary(database),
            [new FakeReconciler(stepId, ProvisioningReconciliationOutcomes.EffectAbsent)]);

        var first = await service.ApplyAsync(reservation.ProvisioningOperationId, stepId, running.Version);
        var secondAttempt = await operations.ApplyCheckpointAsync(new(first.OperationId, first.Version,
            ProvisioningOperationStatuses.Running, stepId, stepId, ProvisioningStepStatuses.Running, ExplicitRetry: true));
        var secondFailed = await operations.ApplyCheckpointAsync(new(secondAttempt.OperationId, secondAttempt.Version,
            ProvisioningOperationStatuses.Failed, stepId, stepId, ProvisioningStepStatuses.Failed,
            ProvisioningFailureTypes.Unknown, "test_unknown", "Unknown test outcome.", KeepActiveSlot: true));
        var second = await service.ApplyAsync(reservation.ProvisioningOperationId, stepId, secondFailed.Version);
        var thirdAttempt = await operations.ApplyCheckpointAsync(new(second.OperationId, second.Version,
            ProvisioningOperationStatuses.Running, stepId, stepId, ProvisioningStepStatuses.Running, ExplicitRetry: true));
        var thirdFailed = await operations.ApplyCheckpointAsync(new(thirdAttempt.OperationId, thirdAttempt.Version,
            ProvisioningOperationStatuses.Failed, stepId, stepId, ProvisioningStepStatuses.Failed,
            ProvisioningFailureTypes.Unknown, "test_unknown", "Unknown test outcome.", KeepActiveSlot: true));

        var applied = await service.ApplyAsync(reservation.ProvisioningOperationId, stepId, thirdFailed.Version);

        Assert.False(applied.IsActive);
        Assert.Null(applied.Steps.Single(item => item.StepId == stepId).ReconciledRetryAttempt);
        Assert.Equal(3, applied.Steps.Single(item => item.StepId == stepId).Attempt);
        Assert.Equal(ProvisioningOperationStatuses.Failed, applied.Status);
    }

    [Fact]
    public async Task ReconciledEffectContinuesWithoutRepeatingMutationAndStopsAtV2Gate()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var operations = OperationStore(database);
        var running = await operations.ApplyCheckpointAsync(new(reservation.ProvisioningOperationId, 1,
            ProvisioningOperationStatuses.Running, ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage, ProvisioningStepStatuses.Running));
        var service = new ProvisioningReconciliationService(operations, new EfCorePersistenceTransactionBoundary(database),
            [new FakeReconciler(ProvisioningStepIds.PrepareStorage, ProvisioningReconciliationOutcomes.EffectExists)]);
        await service.ApplyAsync(reservation.ProvisioningOperationId, ProvisioningStepIds.PrepareStorage, running.Version);
        var prepareInvocations = 0;
        var steps = new IProvisioningStep[]
        {
            new FakeStep(ProvisioningStepIds.PrepareStorage, () => prepareInvocations++),
            new FakeStep(ProvisioningStepIds.ConfigureGame),
            new AcquireImageProvisioningStep(ImageStore(database),
                new UnprovableImageAcquisitionAdapter(),
                Options.Create(new GamesHud.Api.Configuration.RuntimeImageAcquisitionOptions()))
        };
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [], [], ProvisioningPipelines.ImageAcquisition.Steps.Select(item => item.Id).ToArray());
        var context = new ProvisioningContext(reservation.ProvisioningOperationId, Definition(Image(DigestB)), plan,
            reservation);

        var result = await new ProvisioningEngine(operations, steps,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProvisioningEngine>.Instance)
            .ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, prepareInvocations);
        Assert.Equal(RuntimeImageAcquisitionErrorCodes.OutcomeUnknown, result.Failure!.Code);
    }

    [Fact]
    public async Task TypedUnknownMutationKeepsRecoverySlot()
    {
        using var root = TemporaryDirectory.Create();
        await using var firstDatabase = CreateInitialized(root.Path);
        var reservation = await CreateStore(firstDatabase).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var operations = OperationStore(firstDatabase);
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [], [], ProvisioningPipelines.ImageAcquisition.Steps.Select(item => item.Id).ToArray());
        var context = new ProvisioningContext(reservation.ProvisioningOperationId, Definition(Image(DigestA)), plan, reservation);
        var engine = new ProvisioningEngine(operations,
            [new ResultStep(ProvisioningStepIds.PrepareStorage, ProvisioningStepResult.Failure(
                "provider_unknown", "Unknown provider outcome.", ProvisioningFailureTypes.Unknown))],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProvisioningEngine>.Instance);

        await engine.ExecuteAsync(context, CancellationToken.None);
        var unknown = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.True(unknown!.IsActive);
        Assert.Equal(ProvisioningRecoveryDecisions.Reconcile,
            (await new ProvisioningRecoveryService(operations).ClassifyAsync(unknown.OperationId, CancellationToken.None)).Decision);
    }

    [Fact]
    public async Task ProviderInterruptionIsRecoverableWhileExplicitCancellationRemainsDistinct()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var reservation = await CreateStore(database).ReserveProvisioningPlanAsync(Plan(Image(DigestA)));
        var operations = OperationStore(database);
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [], [], ProvisioningPipelines.ImageAcquisition.Steps.Select(item => item.Id).ToArray());
        var interrupted = new ProvisioningContext(reservation.ProvisioningOperationId, Definition(Image(DigestA)), plan,
            reservation, userRequestedCancellation: false);
        var engine = new ProvisioningEngine(operations,
            [new ThrowingStep(ProvisioningStepIds.PrepareStorage, new OperationCanceledException())],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(interrupted, CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.Equal(ProvisioningOperationStatuses.Failed, result.Status);
        Assert.True(operation!.IsActive);
        Assert.Equal(ProvisioningFailureTypes.Unknown,
            operation.Steps.Single(item => item.StepId == ProvisioningStepIds.PrepareStorage).FailureType);
    }

    [Fact]
    public void PersistenceModelHasRestrictiveImageOwnershipAndConcurrency()
    {
        using var root = TemporaryDirectory.Create();
        using var database = CreateContext(root.Path);
        var entity = database.Model.FindEntityType(typeof(RuntimeImageIntentRecord))!;

        Assert.True(entity.FindProperty(nameof(RuntimeImageIntentRecord.Version))!.IsConcurrencyToken);
        Assert.All(entity.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Auth", StringComparison.OrdinalIgnoreCase));
    }

    private static TrustedRuntimeImage Image(string digest, string registry = "docker.io",
        string repository = "trusted/palworld") => new("docker", registry, repository,
            new ApprovedImageDigest(digest), new RuntimeImagePlatform("linux", "amd64"), "catalog-v1");

    private static RuntimeImageOwner Owner(string operationId) => new(operationId, "server-one", "palworld", "docker");

    private static GameDefinition Definition(TrustedRuntimeImage image) => new(new GameId("palworld"), "Palworld", "Test",
        new GameDefinitionBranding("palworld"), ["docker"], ["test"], ports:
        [new GamePortDefinition("game", "Game", 8211, PortProtocols.Udp, true, true, PortExposures.Public, "Test", "test")],
        storages: [new GameStorageDefinition("data", "Data", StoragePurposes.GameData, "/palworld", true, true, true, true, null, "test")],
        runtimeImages: [image]);

    private static ManagedServerProvisioningPlan Plan(TrustedRuntimeImage image) => new(
        "server-one", "palworld", "Managed Palworld", "docker",
        [new("game", "udp", 8211, "public")], [new("data", "servers/server-one/data")],
        PipelineVersion: ProvisioningPipelines.ImageAcquisitionVersion,
        Steps: ProvisioningPipelines.ImageAcquisition.Steps.Select(step => new ProvisioningStepPlan(step.Id,
            step.Sequence, step.RetryClassification, step.SideEffectClassification, step.MaxAttempts, step.Sequence <= 3)).ToArray(),
        RuntimeImage: image);

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

    private static ManagedServerStore CreateStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database));
    private static RuntimeImageIntentStore ImageStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database));
    private static ProvisioningOperationStore OperationStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database), new ProvisioningStateMachine());

    private static GamesHudDbContext CreateInitialized(string root)
    {
        var database = CreateContext(root);
        new PersistenceInitializer(database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = root })),
            Options.Create(new PersistenceOptions { AutoMigrate = true })).InitializeAsync().GetAwaiter().GetResult();
        return database;
    }

    private static GamesHudDbContext CreateContext(string root) => new(new DbContextOptionsBuilder<GamesHudDbContext>()
        .UseSqlite($"Data Source={Path.Combine(root, "system", "gameshud.db")};Pooling=False").Options);

    private sealed class FakeReconciler(string stepId, string outcome) : IProvisioningStepReconciler
    {
        public string StepId { get; } = stepId;
        public Task<ProvisioningReconciliationResult> InspectAsync(ProvisioningOperationSnapshot operation,
            ProvisioningStepSnapshot step, CancellationToken cancellationToken) =>
            Task.FromResult(new ProvisioningReconciliationResult(outcome, "Test evidence."));
    }

    private sealed class UnprovableImageAcquisitionAdapter : IRuntimeImageAcquisitionAdapter
    {
        public Task<RuntimeImageInspectionResult> InspectAsync(TrustedRuntimeImage image, CancellationToken cancellationToken) =>
            Task.FromResult(RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Unprovable));
        public Task<RuntimeImageAcquisitionResult> AcquireAsync(TrustedRuntimeImage image, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Acquisition must not be reached.");
    }

    private sealed class FakeStep(string id, Action? invocation = null) : IProvisioningStep
    {
        public string Id { get; } = id;
        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
        {
            invocation?.Invoke();
            return Task.FromResult(ProvisioningStepResult.Success());
        }
    }

    private sealed class ResultStep(string id, ProvisioningStepResult result) : IProvisioningStep
    {
        public string Id { get; } = id;
        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingStep(string id, Exception exception) : IProvisioningStep
    {
        public string Id { get; } = id;
        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromException<ProvisioningStepResult>(exception);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) { Path = path; }
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-image-foundation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }
        public void Dispose()
        {
            var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            var target = System.IO.Path.GetFullPath(Path);
            if (target.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && target.Contains("gameshud-image-foundation-", StringComparison.Ordinal)) Directory.Delete(target, true);
        }
    }
}
