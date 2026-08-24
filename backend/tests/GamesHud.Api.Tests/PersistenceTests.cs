using System.Net;
using System.Text.Json;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
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
        Assert.Empty(dbContext.Model.GetEntityTypes()
            .Where(entityType => entityType.ClrType != typeof(PersistenceMetadataRecord)));
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
