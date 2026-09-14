using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Palworld.ManagedConfiguration;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Secrets.Models;
using GamesHud.Api.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldManagedConfigurationProvisioningTests
{
    private const string ServerSentinel = "SERVER_SECRET_SENTINEL_GH14";
    private const string AdminSentinel = "ADMIN_SECRET_SENTINEL_GH14";

    [Fact]
    public async Task ConfigureGameReloadsDurableIntentAfterRestartAndWritesOnlyFinalIni()
    {
        using var root = new TemporaryRoot();
        var codec = new PalworldProvisioningConfigurationCodec();
        var serverReference = new SecretReference(SecretId.New());
        var adminReference = new SecretReference(SecretId.New());
        ManagedServerReservationResult reservation;
        await using (var first = CreateInitialized(root.Path))
        {
            var configuration = codec.CreateInitial("Managed Server", serverReference, adminReference);
            reservation = await new ManagedServerStore(first, new EfCorePersistenceTransactionBoundary(first))
                .ReserveProvisioningPlanAsync(CreatePersistencePlan(configuration));
        }
        Directory.CreateDirectory(Path.Combine(root.Path, "servers", "server-one", "data"));

        await using var restarted = CreateContext(root.Path);
        var secretStore = new FakeSecretStore(new Dictionary<SecretReference, string>
        {
            [serverReference] = ServerSentinel,
            [adminReference] = AdminSentinel
        });
        var step = CreateRealStep(restarted, root.Path, codec, secretStore);
        var contextWithoutOriginalConfiguration = CreateContextForExecution(
            gameConfiguration: null, reservation.ProvisioningOperationId, reservation.StorageReservationIds);

        var result = await step.ExecuteAsync(contextWithoutOriginalConfiguration, CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
        var finalPath = Path.Combine(root.Path, "servers", "server-one", "data", "Pal", "Saved", "Config",
            "LinuxServer", "PalWorldSettings.ini");
        var content = await File.ReadAllTextAsync(finalPath);
        Assert.Contains("ServerName=\"Managed Server\"", content, StringComparison.Ordinal);
        Assert.Contains("ServerDescription=\"\"", content, StringComparison.Ordinal);
        Assert.Contains("ServerPlayerMaxNum=32", content, StringComparison.Ordinal);
        Assert.Contains("Difficulty=None", content, StringComparison.Ordinal);
        Assert.Contains($"ServerPassword=\"{ServerSentinel}\"", content, StringComparison.Ordinal);
        Assert.Contains($"AdminPassword=\"{AdminSentinel}\"", content, StringComparison.Ordinal);
        Assert.Equal([serverReference, adminReference], secretStore.RequestedReferences);

        var durablePayload = await restarted.ManagedGameConfigurations.Select(item => item.Payload).SingleAsync();
        var operation = await restarted.ProvisioningOperations.Include(item => item.Steps).SingleAsync();
        var durableText = durablePayload + operation.ErrorMessageSafe
            + string.Join(string.Empty, operation.Steps.Select(item => item.SafeErrorMessage));
        Assert.DoesNotContain(ServerSentinel, durableText, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminSentinel, durableText, StringComparison.Ordinal);
        Assert.DoesNotContain(ServerSentinel, await File.ReadAllTextAsync(DatabasePath(root.Path)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionalPasswordsProduceEmptyValuesWithoutSecretLookup()
    {
        var intent = new FakeIntentReader(new("Server", string.Empty, 32, "None", string.Empty, string.Empty));
        using var root = new TemporaryRoot();
        var target = root.CreateTarget();
        var files = new PalworldManagedConfigurationFileStore(
            new SystemPalworldManagedConfigurationFileSystem(), new PalworldManagedConfigurationSerializer());
        var step = new ConfigurePalworldGameProvisioningStep(new FakeTargetBuilder(target), intent, files,
            NullLogger<ConfigurePalworldGameProvisioningStep>.Instance);

        var result = await step.ExecuteAsync(CreateContextForExecution(null), CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
        var content = await File.ReadAllTextAsync(target.DestinationPath);
        Assert.Contains("ServerPassword=\"\"", content, StringComparison.Ordinal);
        Assert.Contains("AdminPassword=\"\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSecretFailsBeforeFilesystemMutationAndDoesNotLeakReference()
    {
        using var root = new TemporaryRoot();
        var reference = new SecretReference(SecretId.New());
        var codec = new PalworldProvisioningConfigurationCodec();
        var store = new FakeConfigurationStore(codec.CreateInitial("Server", reference));
        var intent = new PalworldManagedConfigurationIntentReader(store, codec,
            new FakeSecretStore(new Dictionary<SecretReference, string>()),
            new PalworldManagedConfigurationSerializer());
        var writer = new TrackingFileStore();
        var step = new ConfigurePalworldGameProvisioningStep(new FakeTargetBuilder(root.CreateTarget()), intent, writer,
            NullLogger<ConfigurePalworldGameProvisioningStep>.Instance);

        var result = await step.ExecuteAsync(CreateContextForExecution(null), CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Failed, result.Status);
        Assert.Equal(PalworldManagedConfigurationErrorCodes.SecretUnavailable, result.ErrorCode);
        Assert.False(writer.Called);
        Assert.DoesNotContain(reference.Id.Value, result.SafeMessage, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers", "server-one", "data", "Pal")));
    }

    [Fact]
    public async Task ReconcilerReturnsTriStateAndMissingSecretIsAmbiguous()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget();
        var expected = new PalworldManagedConfiguration("Server", string.Empty, 32, "None", string.Empty, string.Empty);
        var files = new PalworldManagedConfigurationFileStore(
            new SystemPalworldManagedConfigurationFileSystem(), new PalworldManagedConfigurationSerializer());
        var reconciler = new ConfigurePalworldGameReconciler(
            new FakeTargetBuilder(target), new FakeIntentReader(expected), files);
        var operation = Snapshot();
        var step = operation.Steps.Single();

        Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent,
            (await reconciler.InspectAsync(operation, step, CancellationToken.None)).Outcome);
        await files.MaterializeAsync(target, expected, CancellationToken.None);
        Assert.Equal(ProvisioningReconciliationOutcomes.EffectExists,
            (await reconciler.InspectAsync(operation, step, CancellationToken.None)).Outcome);

        var unavailable = new ConfigurePalworldGameReconciler(new FakeTargetBuilder(target),
            new ThrowingIntentReader(), files);
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await unavailable.InspectAsync(operation, step, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task LegacyExternalTargetFailureNeverReachesIntentOrWriter()
    {
        using var root = new TemporaryRoot();
        var definition = new PalworldGameDefinition();
        var definitions = new GameDefinitionRegistry([definition]);
        var legacy = new ManagedGameServerRecord
        {
            Id = "server-one",
            GameId = "palworld",
            DisplayName = "Legacy",
            InstallationType = ManagedInstallationTypes.External,
            RuntimeType = "docker",
            LifecycleState = "external"
        };
        var targetBuilder = new PalworldManagedConfigurationTargetBuilder(
            new ManagedStorageTargetBuilder(new StaticManagedServerStore(legacy),
                new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = root.Path })), definitions),
            definitions);
        var intent = new ThrowingIntentReader();
        var writer = new TrackingFileStore();
        var step = new ConfigurePalworldGameProvisioningStep(targetBuilder, intent, writer,
            NullLogger<ConfigurePalworldGameProvisioningStep>.Instance);

        var result = await step.ExecuteAsync(CreateContextForExecution(null), CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Failed, result.Status);
        Assert.False(intent.Called);
        Assert.False(writer.Called);
    }

    [Fact]
    public async Task TargetUsesOnlyDataReservationFixedRelativePathAndPalworldMountContract()
    {
        using var root = new TemporaryRoot();
        var managed = new FakeManagedTargetBuilder(root.Path);
        var builder = new PalworldManagedConfigurationTargetBuilder(managed,
            new GameDefinitionRegistry([new PalworldGameDefinition()]));

        var built = await builder.BuildAsync(CreateContextForExecution(null), CancellationToken.None);

        Assert.True(built.Succeeded);
        Assert.Equal(Path.Combine(root.Path, "servers", "server-one", "data"), built.Target!.StorageRoot);
        Assert.Equal(Path.Combine("Pal", "Saved", "Config", "LinuxServer", "PalWorldSettings.ini"),
            Path.GetRelativePath(built.Target.StorageRoot, built.Target.DestinationPath));
        Assert.Equal("/palworld", new PalworldGameDefinition().Storages.Single(item => item.Id == "data").RuntimeTarget);
    }

    [Fact]
    public void ConfigureGameHasNoDockerOrRuntimeMutationDependencyAndPipelineMetadataIsUnchanged()
    {
        var dependencies = typeof(ConfigurePalworldGameProvisioningStep).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType.FullName ?? string.Empty).ToArray();
        Assert.DoesNotContain(dependencies, value => value.Contains("Docker", StringComparison.Ordinal)
            || value.Contains("RuntimeMutation", StringComparison.Ordinal));
        Assert.Equal("gh09-v1", ProvisioningPipeline.Version);
        var definition = ProvisioningPipeline.Steps.Single(item => item.Id == ProvisioningStepIds.ConfigureGame);
        Assert.Equal(5, definition.Sequence);
        Assert.Equal(ProvisioningRetryClassifications.RequiresInspection, definition.RetryClassification);
        Assert.Equal(ProvisioningSideEffectClassifications.Mutation, definition.SideEffectClassification);
        Assert.Equal(1, definition.MaxAttempts);
    }

    private static ConfigurePalworldGameProvisioningStep CreateRealStep(
        GamesHudDbContext database, string root, PalworldProvisioningConfigurationCodec codec, ISecretStore secrets)
    {
        var managedStore = new ManagedServerStore(database, new EfCorePersistenceTransactionBoundary(database));
        var definitions = new GameDefinitionRegistry([new PalworldGameDefinition()]);
        var paths = new ManagedStoragePathBuilder(Options.Create(new StorageOptions { DataRoot = root }));
        var targets = new PalworldManagedConfigurationTargetBuilder(
            new ManagedStorageTargetBuilder(managedStore, paths, definitions), definitions);
        var serializer = new PalworldManagedConfigurationSerializer();
        return new(targets,
            new PalworldManagedConfigurationIntentReader(
                new GameProvisioningConfigurationStore(database), codec, secrets, serializer),
            new PalworldManagedConfigurationFileStore(new SystemPalworldManagedConfigurationFileSystem(), serializer),
            NullLogger<ConfigurePalworldGameProvisioningStep>.Instance);
    }

    private static ProvisioningContext CreateContextForExecution(
        ValidatedGameProvisioningConfiguration? gameConfiguration,
        string operationId = "operation-one",
        IReadOnlyCollection<string>? storageReservationIds = null)
    {
        var definition = new PalworldGameDefinition();
        var plan = new ValidatedProvisioningPlan(new("server-one"), new("palworld"), "Untrusted in-memory name",
            "docker", "compatible", [], [], [new("data", "servers/server-one/data")], [],
            ProvisioningStepIds.All, gameConfiguration);
        return new(operationId, definition, plan,
            new("server-one", operationId, [], storageReservationIds ?? ["storage-one"]));
    }

    private static ManagedServerProvisioningPlan CreatePersistencePlan(
        ValidatedGameProvisioningConfiguration configuration) => new(
        "server-one", "palworld", "Managed Palworld", "docker",
        [new("game", "udp", 8211, "public"), new("query", "udp", 27015, "public")],
        [new("data", "servers/server-one/data")], configuration);

    private static ProvisioningOperationSnapshot Snapshot()
    {
        var step = new ProvisioningStepSnapshot(ProvisioningStepIds.ConfigureGame, 5,
            ProvisioningStepStatuses.Running, 1, ProvisioningRetryClassifications.RequiresInspection,
            ProvisioningSideEffectClassifications.Mutation, 1, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        return new("operation-one", "server-one", ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.ConfigureGame, null, null, DateTimeOffset.UtcNow, null,
            ProvisioningPipeline.Version, 1, true, [step]);
    }

    private static GamesHudDbContext CreateInitialized(string root)
    {
        var database = CreateContext(root);
        new PersistenceInitializer(database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = root })),
            Options.Create(new PersistenceOptions { AutoMigrate = true })).InitializeAsync().GetAwaiter().GetResult();
        return database;
    }

    private static GamesHudDbContext CreateContext(string root) => new(new DbContextOptionsBuilder<GamesHudDbContext>()
        .UseSqlite($"Data Source={DatabasePath(root)};Pooling=False").Options);
    private static string DatabasePath(string root) => Path.Combine(root, "system", "gameshud.db");

    private sealed class FakeSecretStore(IReadOnlyDictionary<SecretReference, string> values) : ISecretStore
    {
        public List<SecretReference> RequestedReferences { get; } = [];
        public Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            RequestedReferences.Add(reference);
            return values.TryGetValue(reference, out var value)
                ? Task.FromResult(SecretValue.FromPlainText(value))
                : Task.FromException<SecretValue>(new SecretNotFoundException());
        }
        public Task<SecretReference> StoreAsync(SecretPurpose purpose, SecretValue value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceAsync(SecretReference reference, SecretValue value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeConfigurationStore(ValidatedGameProvisioningConfiguration configuration)
        : IGameProvisioningConfigurationStore
    {
        public Task<ValidatedGameProvisioningConfiguration> LoadAsync(string gameServerId,
            IGameProvisioningConfigurationCodec codec, CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);
        public Task<ValidatedGameProvisioningConfiguration> EnsureAsync(string gameServerId,
            ValidatedGameProvisioningConfiguration value, IGameProvisioningConfigurationCodec codec,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeIntentReader(PalworldManagedConfiguration value)
        : IPalworldManagedConfigurationIntentReader
    {
        public Task<PalworldManagedConfiguration> ReadAsync(GameServerId gameServerId,
            CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class ThrowingIntentReader : IPalworldManagedConfigurationIntentReader
    {
        public bool Called { get; private set; }
        public Task<PalworldManagedConfiguration> ReadAsync(GameServerId gameServerId,
            CancellationToken cancellationToken)
        {
            Called = true;
            throw new SecretNotFoundException();
        }
    }

    private sealed class FakeTargetBuilder(PalworldManagedConfigurationTarget target)
        : IPalworldManagedConfigurationTargetBuilder
    {
        public Task<PalworldManagedConfigurationTargetBuildResult> BuildAsync(ProvisioningContext context,
            CancellationToken cancellationToken) => Task.FromResult(new PalworldManagedConfigurationTargetBuildResult(target, null, null));
        public Task<PalworldManagedConfigurationTargetBuildResult> BuildForReconciliationAsync(string operationId,
            GameServerId gameServerId, CancellationToken cancellationToken) =>
            Task.FromResult(new PalworldManagedConfigurationTargetBuildResult(target, null, null));
    }

    private sealed class StaticManagedServerStore(ManagedGameServerRecord server) : IManagedServerStore
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

    private sealed class TrackingFileStore : IPalworldManagedConfigurationFileStore
    {
        public bool Called { get; private set; }
        public Task<PalworldManagedConfigurationMutationResult> MaterializeAsync(PalworldManagedConfigurationTarget target,
            PalworldManagedConfiguration expected, CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException();
        }
        public Task<ProvisioningReconciliationResult> InspectAsync(PalworldManagedConfigurationTarget target,
            PalworldManagedConfiguration expected, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeManagedTargetBuilder(string root) : IManagedStorageTargetBuilder
    {
        private ManagedStorageTargetBuildResult Result(string operationId, GameServerId serverId)
        {
            var data = Path.Combine(root, "servers", "server-one", "data");
            return new(new(serverId, operationId, root,
                [new("storage-one", "data", "servers/server-one/data", data),
                 new("other", "backups", "servers/server-one/backups", Path.Combine(root, "servers", "server-one", "backups"))]), null, null);
        }
        public Task<ManagedStorageTargetBuildResult> BuildAsync(ProvisioningContext context,
            CancellationToken cancellationToken) => Task.FromResult(Result(context.OperationId, context.GameServerId));
        public Task<ManagedStorageTargetBuildResult> BuildForReconciliationAsync(string operationId,
            GameServerId gameServerId, CancellationToken cancellationToken) => Task.FromResult(Result(operationId, gameServerId));
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshud-gh14-provisioning", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "servers", "server-one", "data"));
        }
        public string Path { get; }
        public PalworldManagedConfigurationTarget CreateTarget()
        {
            var storage = System.IO.Path.Combine(Path, "servers", "server-one", "data");
            var directory = System.IO.Path.Combine(storage, "Pal", "Saved", "Config", "LinuxServer");
            return new(new("server-one"), "operation-one", Path, storage, directory,
                System.IO.Path.Combine(directory, "PalWorldSettings.ini"),
                ".PalWorldSettings.ini.gameshud-operation-one-");
        }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
