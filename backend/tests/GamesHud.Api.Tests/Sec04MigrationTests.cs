using GamesHud.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GamesHud.Api.Tests;

public sealed class Sec04MigrationTests
{
    private const string PreviousMigration = "20260922180100_AddManagedGameServerRequestIdempotency";

    [Fact]
    public async Task UpgradeCreatesIdentityAndOwnershipWithoutClaimingHistoricalRows()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gameshud-sec04-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<GamesHudDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options;
            await using (var database = new GamesHudDbContext(options))
            {
                var migrator = database.Database.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO managed_game_servers (Id, GameId, DisplayName, InstallationType, RuntimeType, LifecycleState, CreatedAtUtc, UpdatedAtUtc) VALUES ({"historical"}, {"palworld"}, {"Historical"}, {"managed"}, {"docker"}, {"pending_provisioning"}, {"2026-09-24T00:00:00Z"}, {"2026-09-24T00:00:00Z"}); INSERT INTO provisioning_operations (Id, GameServerId, Type, Status, ActiveSlot, CurrentStep, StartedAtUtc, UpdatedAtUtc, PipelineVersion, Version) VALUES ({"historical-op"}, {"historical"}, {"provision"}, {"pending"}, {"active"}, {"reserve_resources"}, {"2026-09-24T00:00:00Z"}, {"2026-09-24T00:00:00Z"}, {"gh15-v2"}, {1})");
                await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO managed_game_server_requests (Id, IdempotencyKeyHash, RequestFingerprint, GameServerId, ProvisioningOperationId, CreatedAtUtc) VALUES ({"historical-request"}, {new string('a', 64)}, {new string('b', 64)}, {"historical"}, {"historical-op"}, {"2026-09-24T00:00:00Z"})");

                await migrator.MigrateAsync();
                await database.Database.OpenConnectionAsync();

                Assert.True(await TableExists(database, "AspNetUsers"));
                Assert.Null(await Scalar(database, "SELECT OwnerId FROM managed_game_servers WHERE Id='historical'"));
                Assert.Null(await Scalar(database, "SELECT OwnerId FROM managed_game_server_requests WHERE Id='historical-request'"));
                var indexes = await IndexSql(database);
                Assert.Contains(indexes, sql => sql.Contains("OwnerId", StringComparison.Ordinal)
                    && sql.Contains("IdempotencyKeyHash", StringComparison.Ordinal));
                Assert.Contains(indexes, sql => sql.Contains("WHERE \"OwnerId\" IS NULL", StringComparison.Ordinal));
                Assert.True(await HasOwnerForeignKey(database, "managed_game_servers"));
                Assert.True(await HasOwnerForeignKey(database, "managed_game_server_requests"));
                await database.Database.CloseConnectionAsync();
            }
            SqliteConnection.ClearAllPools();
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task OwnerScopedUniqueIndexAllowsSameHashAcrossOwnersAndProtectsLegacyNamespace()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GamesHudDbContext>().UseSqlite(connection).Options;
        await using var database = new GamesHudDbContext(options);
        await database.Database.MigrateAsync();
        await InsertUser(database, "user-a"); await InsertUser(database, "user-b");
        await InsertServerAndOperation(database, "server-a", "operation-a", "user-a");
        await InsertServerAndOperation(database, "server-b", "operation-b", "user-b");
        await InsertServerAndOperation(database, "legacy-a", "legacy-operation-a");
        await InsertServerAndOperation(database, "legacy-b", "legacy-operation-b");
        var hash = new string('c', 64); var fingerprint = new string('d', 64);
        await InsertRequest(database, "request-a", "user-a", hash, fingerprint, "server-a", "operation-a");
        await InsertRequest(database, "request-b", "user-b", hash, fingerprint, "server-b", "operation-b");
        await InsertRequest(database, "legacy-request-a", null, hash, fingerprint, "legacy-a", "legacy-operation-a");
        await Assert.ThrowsAsync<SqliteException>(() => InsertRequest(database, "legacy-request-b", null,
            hash, fingerprint, "legacy-b", "legacy-operation-b"));
        Assert.Equal(3, await database.ManagedGameServerRequests.CountAsync());
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM AspNetUsers WHERE Id={"user-a"}"));
        Assert.True(await database.Users.AnyAsync(user => user.Id == "user-a"));
    }

    [Fact]
    public async Task ConcurrentDifferentOwnersCanCommitIndependentIntentsWithTheSameKeyHash()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gameshud-sec04-owner-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={file};Pooling=False;Default Timeout=30";
            var options = new DbContextOptionsBuilder<GamesHudDbContext>().UseSqlite(connectionString).Options;
            await using (var setup = new GamesHudDbContext(options))
            {
                await setup.Database.MigrateAsync();
                await InsertUser(setup, "user-a");
                await InsertUser(setup, "user-b");
            }

            var hash = new string('e', 64);
            await Task.WhenAll(
                InsertIntentAtomically(options, "user-a", "server-a", "operation-a", "request-a", hash,
                    new string('a', 64)),
                InsertIntentAtomically(options, "user-b", "server-b", "operation-b", "request-b", hash,
                    new string('b', 64)));

            await using var verification = new GamesHudDbContext(options);
            Assert.Equal(2, await verification.ManagedGameServers.CountAsync());
            Assert.Equal(2, await verification.ProvisioningOperations.CountAsync());
            Assert.Equal(2, await verification.ManagedGameServerRequests.CountAsync());
            Assert.All(await verification.ManagedGameServerRequests.AsNoTracking().ToArrayAsync(), request =>
                Assert.Equal(request.OwnerId,
                    verification.ManagedGameServers.AsNoTracking().Single(server => server.Id == request.GameServerId).OwnerId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static async Task InsertIntentAtomically(DbContextOptions<GamesHudDbContext> options, string owner,
        string server, string operation, string request, string hash, string fingerprint)
    {
        await using var database = new GamesHudDbContext(options);
        await database.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=30000;");
        await using var transaction = await database.Database.BeginTransactionAsync();
        await InsertServerAndOperation(database, server, operation, owner);
        await InsertRequest(database, request, owner, hash, fingerprint, server, operation);
        await transaction.CommitAsync();
    }

    private static Task InsertUser(GamesHudDbContext database, string id) => database.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO AspNetUsers (Id, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES ({id}, {false}, {false}, {false}, {true}, {0})");

    private static Task InsertServerAndOperation(GamesHudDbContext database, string server, string operation, string? owner = null) =>
        database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO managed_game_servers (Id, GameId, DisplayName, InstallationType, RuntimeType, LifecycleState, CreatedAtUtc, UpdatedAtUtc, OwnerId) VALUES ({server}, {"palworld"}, {server}, {"managed"}, {"docker"}, {"pending_provisioning"}, {"2026-09-24T00:00:00Z"}, {"2026-09-24T00:00:00Z"}, {owner}); INSERT INTO provisioning_operations (Id, GameServerId, Type, Status, ActiveSlot, CurrentStep, StartedAtUtc, UpdatedAtUtc, PipelineVersion, Version) VALUES ({operation}, {server}, {"provision"}, {"pending"}, {"active"}, {"reserve_resources"}, {"2026-09-24T00:00:00Z"}, {"2026-09-24T00:00:00Z"}, {"gh15-v2"}, {1})");

    private static Task InsertRequest(GamesHudDbContext database, string id, string? owner, string hash,
        string fingerprint, string server, string operation) => database.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO managed_game_server_requests (Id, OwnerId, IdempotencyKeyHash, RequestFingerprint, GameServerId, ProvisioningOperationId, CreatedAtUtc) VALUES ({id}, {owner}, {hash}, {fingerprint}, {server}, {operation}, {"2026-09-24T00:00:00Z"})");

    private static async Task<bool> TableExists(GamesHudDbContext database, string table) =>
        Convert.ToInt32(await Scalar(database, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'")) == 1;

    private static async Task<object?> Scalar(GamesHudDbContext database, string sql)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand(); command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private static async Task<IReadOnlyCollection<string>> IndexSql(GamesHudDbContext database)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='managed_game_server_requests' AND sql IS NOT NULL";
        await using var reader = await command.ExecuteReaderAsync(); var results = new List<string>();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0)); return results;
    }

    private static async Task<bool> HasOwnerForeignKey(GamesHudDbContext database, string table)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list('{table}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (reader.GetString(2) == "AspNetUsers" && reader.GetString(3) == "OwnerId"
                && reader.GetString(6).Equals("RESTRICT", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
