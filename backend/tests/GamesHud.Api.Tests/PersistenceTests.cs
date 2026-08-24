using System.Net;
using System.Text.Json;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task InitializerCreatesMigratedSqliteDatabaseInsideDataRoot()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out var layout);
        var initializer = CreateInitializer(dbContext, tempRoot.Path);

        await initializer.InitializeAsync();

        Assert.True(File.Exists(layout.DatabasePath));
        Assert.Equal(Path.Combine(tempRoot.Path, "system", "gameshud.db"), layout.DatabasePath);
        Assert.StartsWith(Path.GetFullPath(tempRoot.Path), layout.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.True((await dbContext.Database.GetAppliedMigrationsAsync()).Any());
    }

    [Fact]
    public async Task InitializerWritesOnlyTechnicalSchemaMetadata()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);
        var initializer = CreateInitializer(dbContext, tempRoot.Path);

        await initializer.InitializeAsync();

        var metadata = await dbContext.PersistenceMetadata.SingleAsync();

        Assert.Equal(PersistenceMetadataRecord.SchemaMetadataId, metadata.Id);
        Assert.Equal("persistence-foundation", metadata.Value);
        Assert.Equal(TimeSpan.Zero, metadata.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, metadata.UpdatedAt.Offset);
    }

    [Fact]
    public async Task HealthServiceReportsReadyWhenMigrationsAreCurrent()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);
        var initializer = CreateInitializer(dbContext, tempRoot.Path);

        await initializer.InitializeAsync();
        var status = await new PersistenceHealthService(dbContext).GetStatusAsync();

        Assert.True(status.Available);
        Assert.Equal(PersistenceHealthService.ProviderName, status.Provider);
        Assert.Equal("up_to_date", status.MigrationStatus);
        Assert.NotNull(status.AppliedMigration);
        Assert.Null(status.ErrorCode);
    }

    [Fact]
    public async Task HealthServiceReportsUnavailableWithoutSensitiveDetailsWhenDatabaseCannotOpen()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var fileDataRoot = Path.Combine(tempRoot.Path, "not-a-directory");
        await File.WriteAllTextAsync(fileDataRoot, "occupied");
        await using var dbContext = CreateDbContext(fileDataRoot, out _);

        var status = await new PersistenceHealthService(dbContext).GetStatusAsync();

        Assert.False(status.Available);
        Assert.Equal(PersistenceHealthService.ProviderName, status.Provider);
        Assert.Equal("unavailable", status.MigrationStatus);
        Assert.Equal("persistence_unavailable", status.ErrorCode);
        Assert.Null(status.AppliedMigration);
    }

    [Fact]
    public async Task EndpointReturnsPersistenceStatusWithoutPathsOrConnectionStrings()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/system/persistence");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(tempRoot.Path, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gameshud.db", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndpointDoesNotAcceptClientControlledDatabasePath()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/system/persistence?databasePath=C:/escape/gameshud.db");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("escape", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetadataPrimaryKeyPreventsDuplicateCreation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);
        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();

        dbContext.ChangeTracker.Clear();
        dbContext.PersistenceMetadata.Add(new PersistenceMetadataRecord
        {
            Id = PersistenceMetadataRecord.SchemaMetadataId,
            Value = "duplicate",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task TransactionBoundaryCommitsSuccessfulOperation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);
        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();
        var transactionBoundary = new EfCorePersistenceTransactionBoundary(dbContext);

        await transactionBoundary.ExecuteAsync((database, _) =>
        {
            database.PersistenceMetadata.Add(new PersistenceMetadataRecord
            {
                Id = "transaction-commit",
                Value = "committed",
            });

            return Task.CompletedTask;
        });

        Assert.True(await dbContext.PersistenceMetadata.AnyAsync(record => record.Id == "transaction-commit"));
    }

    [Fact]
    public async Task TransactionBoundaryRollsBackFailedOperation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);
        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();
        var transactionBoundary = new EfCorePersistenceTransactionBoundary(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactionBoundary.ExecuteAsync(async (database, cancellationToken) =>
            {
                database.PersistenceMetadata.Add(new PersistenceMetadataRecord
                {
                    Id = "transaction-rollback",
                    Value = "rolled-back",
                });
                await database.SaveChangesAsync(cancellationToken);

                throw new InvalidOperationException("rollback");
            }));

        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.PersistenceMetadata.AnyAsync(record => record.Id == "transaction-rollback"));
    }

    [Fact]
    public async Task InitializerDoesNotPersistSecretsOrLegacyPalworldResources()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);

        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();
        var json = JsonSerializer.Serialize(await dbContext.PersistenceMetadata.ToListAsync());

        Assert.DoesNotContain("ServerPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AdminPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Webhook", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("palworld", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Amigos e Amigos", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(await dbContext.ManagedGameServers.AnyAsync());
        Assert.False(await dbContext.PortReservations.AnyAsync());
        Assert.False(await dbContext.StorageReservations.AnyAsync());
        Assert.False(await dbContext.ProvisioningOperations.AnyAsync());
    }

    [Fact]
    public async Task InitializerDoesNotCreateGameServerDirectoriesOrTouchDocker()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out _);

        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();

        Assert.True(Directory.Exists(Path.Combine(tempRoot.Path, "system")));
        Assert.False(Directory.Exists(Path.Combine(tempRoot.Path, "servers")));
    }

    [Fact]
    public async Task StoreCreatesManagedServerReservationsAndPendingOperationAtomically()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        var result = await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));

        var server = await store.GetManagedServerAsync("SERVER-ONE");
        var activeOperation = await store.GetActiveOperationAsync("server-one");

        Assert.NotNull(server);
        Assert.Equal("server-one", result.GameServerId);
        Assert.Equal("server-one", server.Id);
        Assert.Equal("palworld", server.GameId);
        Assert.Equal(ManagedInstallationTypes.Managed, server.InstallationType);
        Assert.Equal(ManagedGameServerLifecycleStates.PendingProvisioning, server.LifecycleState);
        Assert.Equal(2, server.PortReservations.Count);
        Assert.Single(server.StorageReservations);
        Assert.Single(server.ProvisioningOperations);
        Assert.NotNull(activeOperation);
        Assert.Equal(result.ProvisioningOperationId, activeOperation.Id);
        Assert.Equal(ProvisioningOperationStatuses.Pending, activeOperation.Status);
        Assert.Equal(ProvisioningOperationActiveSlots.Active, activeOperation.ActiveSlot);
    }

    [Fact]
    public async Task GameServerIdIsUniqueAndNormalized()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using (var dbContext = CreateInitializedDbContext(tempRoot.Path))
        {
            await CreateStore(dbContext).ReserveProvisioningPlanAsync(CreatePlan("Server-One"));
        }

        await using var duplicateContext = CreateDbContext(tempRoot.Path, out _);
        var store = CreateStore(duplicateContext);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan("server-one", tcpPort: 8215)));

        await using var verification = CreateDbContext(tempRoot.Path, out _);
        Assert.Single(verification.ManagedGameServers);
        Assert.False(await verification.ManagedGameServers.AnyAsync(server => server.Id == "Server-One"));
    }

    [Fact]
    public async Task SameTcpAndUdpNumericPortCanCoexist()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan(
            "server-one",
            tcpPort: 8211,
            udpPort: 8211));

        Assert.Equal(2, await dbContext.PortReservations.CountAsync(reservation => reservation.Port == 8211));
        Assert.Contains(dbContext.PortReservations, reservation => reservation.Protocol == "tcp");
        Assert.Contains(dbContext.PortReservations, reservation => reservation.Protocol == "udp");
    }

    [Fact]
    public async Task DuplicateTcpPortIsRejectedAndRollsBackWholeReservation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one", tcpPort: 8211));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan("server-two", tcpPort: 8211, udpPort: 8212)));

        await using var verification = CreateDbContext(tempRoot.Path, out _);
        Assert.False(await verification.ManagedGameServers.AnyAsync(server => server.Id == "server-two"));
        Assert.False(await verification.ProvisioningOperations.AnyAsync(operation => operation.GameServerId == "server-two"));
        Assert.False(await verification.PortReservations.AnyAsync(reservation => reservation.GameServerId == "server-two"));
        Assert.False(await verification.StorageReservations.AnyAsync(reservation => reservation.GameServerId == "server-two"));
    }

    [Fact]
    public async Task DuplicateUdpPortIsRejected()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one", udpPort: 8211));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan("server-two", tcpPort: 8212, udpPort: 8211)));
    }

    [Fact]
    public async Task DuplicateStorageRelativePathIsRejectedAndRollsBackWholeReservation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one", storagePath: "servers/shared/data"));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan(
                "server-two",
                tcpPort: 8215,
                udpPort: 8216,
                storagePath: "servers/shared/data")));

        await using var verification = CreateDbContext(tempRoot.Path, out _);
        Assert.False(await verification.ManagedGameServers.AnyAsync(server => server.Id == "server-two"));
        Assert.False(await verification.StorageReservations.AnyAsync(reservation => reservation.GameServerId == "server-two"));
    }

    [Fact]
    public async Task InvalidPortAndAbsoluteStoragePathAreRejectedBeforePersistence()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan("server-one", tcpPort: 70_000)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ReserveProvisioningPlanAsync(CreatePlan("server-two", storagePath: "C:/escape")));

        Assert.False(await dbContext.ManagedGameServers.AnyAsync());
    }

    [Fact]
    public async Task ForeignKeysRejectReservationsAndOperationsWithoutServer()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);

        dbContext.ProvisioningOperations.Add(new ProvisioningOperationRecord
        {
            Id = CreateId(),
            GameServerId = "missing",
            Type = ProvisioningOperationTypes.Provision,
            Status = ProvisioningOperationStatuses.Pending,
            ActiveSlot = ProvisioningOperationActiveSlots.Active,
            CurrentStep = "reserved",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ReservationForeignKeyRejectsMissingOperation()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        dbContext.ManagedGameServers.Add(new ManagedGameServerRecord
        {
            Id = "server-one",
            GameId = "palworld",
            DisplayName = "Managed Palworld",
            InstallationType = ManagedInstallationTypes.Managed,
            RuntimeType = "docker",
            LifecycleState = ManagedGameServerLifecycleStates.PendingProvisioning,
        });
        await dbContext.SaveChangesAsync();

        dbContext.PortReservations.Add(new PortReservationRecord
        {
            Id = CreateId(),
            GameServerId = "server-one",
            PortDefinitionId = "game",
            Protocol = "tcp",
            Port = 8211,
            Exposure = "public",
            Status = ReservationStatuses.Reserved,
            ProvisioningOperationId = "missing-operation",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ActiveOperationGuardRejectsSecondActiveProvisionForSameServer()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));
        dbContext.ProvisioningOperations.Add(new ProvisioningOperationRecord
        {
            Id = CreateId(),
            GameServerId = "server-one",
            Type = ProvisioningOperationTypes.Provision,
            Status = ProvisioningOperationStatuses.Running,
            ActiveSlot = ProvisioningOperationActiveSlots.Active,
            CurrentStep = "duplicate-active",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task TerminalOperationsCanCoexistBecauseActiveSlotIsCleared()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));
        var activeOperation = await dbContext.ProvisioningOperations.SingleAsync();
        activeOperation.Status = ProvisioningOperationStatuses.Failed;
        activeOperation.ActiveSlot = null;
        activeOperation.CompletedAtUtc = DateTimeOffset.UtcNow;
        dbContext.ProvisioningOperations.Add(new ProvisioningOperationRecord
        {
            Id = CreateId(),
            GameServerId = "server-one",
            Type = ProvisioningOperationTypes.Provision,
            Status = ProvisioningOperationStatuses.Failed,
            ActiveSlot = null,
            CurrentStep = "terminal-history",
            CompletedAtUtc = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.ProvisioningOperations.CountAsync());
    }

    [Fact]
    public async Task UtcTimestampsAreAppliedToManagedSchema()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));

        var server = await dbContext.ManagedGameServers.SingleAsync();
        var port = await dbContext.PortReservations.FirstAsync();
        var storage = await dbContext.StorageReservations.SingleAsync();
        var operation = await dbContext.ProvisioningOperations.SingleAsync();

        Assert.Equal(TimeSpan.Zero, server.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, server.UpdatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, port.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, storage.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, operation.StartedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, operation.UpdatedAtUtc.Offset);
    }

    [Fact]
    public async Task RunningOperationSurvivesConceptualProcessRestart()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using (var dbContext = CreateInitializedDbContext(tempRoot.Path))
        {
            var store = CreateStore(dbContext);
            await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));
            var operation = await dbContext.ProvisioningOperations.SingleAsync();
            operation.Status = ProvisioningOperationStatuses.Running;
            operation.CurrentStep = "creating-container";
            await dbContext.SaveChangesAsync();
        }

        await using var reloaded = CreateDbContext(tempRoot.Path, out _);
        var runningOperation = await reloaded.ProvisioningOperations.SingleAsync();

        Assert.Equal(ProvisioningOperationStatuses.Running, runningOperation.Status);
        Assert.Equal("creating-container", runningOperation.CurrentStep);
        Assert.Equal(ProvisioningOperationActiveSlots.Active, runningOperation.ActiveSlot);
    }

    [Fact]
    public async Task ManagedSchemaDoesNotContainUsersAuthOrSecretColumns()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var modelText = JsonSerializer.Serialize(dbContext.Model.GetEntityTypes()
            .Select(entityType => new
            {
                entityType.ClrType.Name,
                Properties = entityType.GetProperties().Select(property => property.Name)
            }));

        Assert.DoesNotContain("User", modelText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Role", modelText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tenant", modelText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", modelText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Webhook", modelText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", modelText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReservationDoesNotCreateDirectoriesDockerResourcesOrLegacyRecords()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateInitializedDbContext(tempRoot.Path);
        var store = CreateStore(dbContext);

        await store.ReserveProvisioningPlanAsync(CreatePlan("server-one"));

        Assert.False(Directory.Exists(Path.Combine(tempRoot.Path, "servers")));
        Assert.False(await dbContext.ManagedGameServers.AnyAsync(server =>
            server.Id == "palworld" || server.DisplayName == "Amigos e Amigos"));
    }

    private static GamesHudDbContext CreateDbContext(string dataRoot, out PersistenceLayout layout)
    {
        var resolver = new PersistenceLayoutResolver(Options.Create(new StorageOptions
        {
            DataRoot = dataRoot,
        }));
        layout = resolver.ResolveLayout();
        var options = new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(layout.DatabasePath))
            .Options;

        return new GamesHudDbContext(options);
    }

    private static GamesHudDbContext CreateInitializedDbContext(string dataRoot)
    {
        var dbContext = CreateDbContext(dataRoot, out _);
        CreateInitializer(dbContext, dataRoot).InitializeAsync().GetAwaiter().GetResult();

        return dbContext;
    }

    private static ManagedServerStore CreateStore(GamesHudDbContext dbContext)
    {
        return new ManagedServerStore(
            dbContext,
            new EfCorePersistenceTransactionBoundary(dbContext));
    }

    private static ManagedServerProvisioningPlan CreatePlan(
        string gameServerId,
        int tcpPort = 8211,
        int udpPort = 8211,
        string? storagePath = null)
    {
        return new ManagedServerProvisioningPlan(
            gameServerId,
            "palworld",
            "Managed Palworld",
            "docker",
            [
                new PortReservationPlan("game", "tcp", tcpPort, "public"),
                new PortReservationPlan("query", "udp", udpPort, "public"),
            ],
            [
                new StorageReservationPlan("data", storagePath),
            ]);
    }

    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static PersistenceInitializer CreateInitializer(GamesHudDbContext dbContext, string dataRoot)
    {
        return new PersistenceInitializer(
            dbContext,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions
            {
                DataRoot = dataRoot,
            })),
            Options.Create(new PersistenceOptions
            {
                AutoMigrate = true,
            }));
    }

    private static WebApplicationFactory<Program> CreateFactory(string dataRoot)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<StorageOptions>(options => options.DataRoot = dataRoot);
                });
            });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"gameshud-persistence-tests-{Guid.NewGuid():N}");

            Directory.CreateDirectory(path);

            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
