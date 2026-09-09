using System.Text.Json;
using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Secrets.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class DurableGameConfigurationTests
{
    [Fact]
    public async Task ConfigurationAndExplicitSecretRolesSurviveDatabaseRestartWithoutPlaintext()
    {
        using var root = TemporaryDirectory.Create();
        var codec = new PalworldProvisioningConfigurationCodec();
        var serverPassword = new SecretReference(SecretId.New());
        var adminPassword = new SecretReference(SecretId.New());
        _ = SecretValue.FromPlainText("SYNTHETIC_PASSWORD_SENTINEL_RUNTIME_ONLY");
        await using (var database = CreateInitialized(root.Path))
        {
            await CreateStore(database).ReserveProvisioningPlanAsync(CreatePlan(
                codec.CreateInitial("Managed Palworld", serverPassword, adminPassword)));
        }

        await using var reloaded = CreateContext(root.Path);
        var restored = await new GameProvisioningConfigurationStore(reloaded)
            .LoadAsync("server-one", codec);
        var typed = Assert.IsType<PalworldInitialProvisioningConfiguration>(restored.TypedValue);

        Assert.Equal(serverPassword, typed.ServerPasswordSecretReference);
        Assert.Equal(adminPassword, typed.AdminPasswordSecretReference);
        Assert.Equal(1, restored.SchemaVersion);
        var record = await reloaded.ManagedGameConfigurations.SingleAsync();
        Assert.DoesNotContain("SYNTHETIC_PASSWORD_SENTINEL_RUNTIME_ONLY", record.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC_PASSWORD_SENTINEL_RUNTIME_ONLY",
            await File.ReadAllTextAsync(DatabasePath(root.Path)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerOperationReservationsAndConfigurationAreCreatedTransactionally()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var codec = new PalworldProvisioningConfigurationCodec();

        await CreateStore(database).ReserveProvisioningPlanAsync(CreatePlan(codec.CreateInitial("Server")));

        Assert.Equal(1, await database.ManagedGameServers.CountAsync());
        Assert.Equal(1, await database.ProvisioningOperations.CountAsync());
        Assert.Equal(1, await database.ManagedGameConfigurations.CountAsync());
        Assert.Equal(2, await database.PortReservations.CountAsync());
        Assert.Equal(1, await database.StorageReservations.CountAsync());
    }

    [Fact]
    public async Task SameIntentIsIdempotentAndDifferentIntentConflicts()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        var codec = new PalworldProvisioningConfigurationCodec();
        var initial = codec.CreateInitial("Server");
        await CreateStore(database).ReserveProvisioningPlanAsync(CreatePlan(initial));
        var configurations = new GameProvisioningConfigurationStore(database);

        await configurations.EnsureAsync("server-one", initial, codec);
        var exception = await Assert.ThrowsAsync<GameConfigurationException>(() =>
            configurations.EnsureAsync("server-one", codec.CreateInitial("Different"), codec));

        Assert.Equal(GameConfigurationErrorCodes.Conflict, exception.Code);
        Assert.Equal(1, await database.ManagedGameConfigurations.CountAsync());
    }

    [Theory]
    [InlineData("other", PalworldProvisioningConfigurationCodec.InitialConfigurationKind, 1, "{}", GameConfigurationErrorCodes.Invalid)]
    [InlineData("palworld", "other.initial", 1, "{}", GameConfigurationErrorCodes.Invalid)]
    [InlineData("palworld", PalworldProvisioningConfigurationCodec.InitialConfigurationKind, 2, "{}", GameConfigurationErrorCodes.VersionUnsupported)]
    [InlineData("palworld", PalworldProvisioningConfigurationCodec.InitialConfigurationKind, 1, "{broken", GameConfigurationErrorCodes.Invalid)]
    public void CodecFailsClosedForWrongIdentityVersionOrPayload(string gameId, string kind, int version,
        string payload, string expectedCode)
    {
        var exception = Assert.Throws<GameConfigurationException>(() =>
            new PalworldProvisioningConfigurationCodec().Deserialize(gameId, kind, version, payload));
        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain(payload, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedModelsCannotContainSecretValue()
    {
        Assert.DoesNotContain(typeof(ManagedGameConfigurationRecord).GetProperties(), property =>
            property.PropertyType == typeof(SecretValue));
        Assert.DoesNotContain(typeof(PalworldInitialProvisioningConfiguration).GetProperties(), property =>
            property.PropertyType == typeof(SecretValue));
    }

    [Fact]
    public void SchemaDefinesUniqueIntentAndOptimisticConcurrency()
    {
        using var root = TemporaryDirectory.Create();
        using var database = CreateContext(root.Path);
        var entity = database.Model.FindEntityType(typeof(ManagedGameConfigurationRecord))!;
        Assert.True(entity.FindProperty(nameof(ManagedGameConfigurationRecord.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(ManagedGameConfigurationRecord.GameServerId), nameof(ManagedGameConfigurationRecord.ConfigurationKind)]));
    }

    [Fact]
    public async Task LegacyExternalAndCrossServerOwnershipAreRejected()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitialized(root.Path);
        database.ManagedGameServers.AddRange(
            new ManagedGameServerRecord { Id = "legacy", GameId = "palworld", DisplayName = "Legacy", InstallationType = ManagedInstallationTypes.External, RuntimeType = "docker", LifecycleState = "external" },
            new ManagedGameServerRecord { Id = "other", GameId = "other", DisplayName = "Other", InstallationType = ManagedInstallationTypes.Managed, RuntimeType = "docker", LifecycleState = "pending" });
        await database.SaveChangesAsync();
        var store = new GameProvisioningConfigurationStore(database);
        var codec = new PalworldProvisioningConfigurationCodec();

        await Assert.ThrowsAsync<GameConfigurationException>(() => store.EnsureAsync("legacy", codec.CreateInitial("Legacy"), codec));
        await Assert.ThrowsAsync<GameConfigurationException>(() => store.EnsureAsync("other", codec.CreateInitial("Other"), codec));
        Assert.Empty(database.ManagedGameConfigurations);
    }

    private static ManagedServerProvisioningPlan CreatePlan(ValidatedGameProvisioningConfiguration configuration) => new(
        "server-one", "palworld", "Managed Palworld", "docker",
        [new("game", "udp", 8211, "public"), new("query", "udp", 27015, "public")],
        [new("data", "servers/server-one/data")], configuration);

    private static ManagedServerStore CreateStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database));

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

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) { Path = path; }
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-config-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            var target = System.IO.Path.GetFullPath(Path);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && target.Contains("gameshud-config-tests-", StringComparison.Ordinal))
                Directory.Delete(target, true);
        }
    }
}
