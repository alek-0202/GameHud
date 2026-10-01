using System.Net;
using System.Net.Http.Json;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GamesHud.Api.Tests;

public sealed class ManagedGameServerApiTests
{
    [Fact]
    public async Task PostRequiresIdempotencyKeyAndReturnsAcceptedLocation()
    {
        await using var factory = CreateFactory(new StubApplication());
        using var client = factory.CreateClient();
        client.AuthenticateAs("user-a");
        var csrf = await client.GetCsrfTokenAsync();
        using var missingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
        { Content = JsonContent.Create(new { gameId = "palworld", displayName = "A" }) };
        missingRequest.AddCsrf(csrf);
        using var missing = await client.SendAsync(missingRequest);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
        { Content = JsonContent.Create(new { gameId = "palworld", displayName = "A", gameServerId = "attacker" }) };
        request.Headers.Add("Idempotency-Key", "create-a");
        request.AddCsrf(csrf);
        using var accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("/api/game-servers/gs-approved", accepted.Headers.Location?.PathAndQuery);
        var body = await accepted.Content.ReadFromJsonAsync<CreateManagedGameServerResponse>();
        Assert.Equal("gs-approved", body?.GameServerId);
    }

    [Fact]
    public async Task ConflictAndUnsupportedFailuresHaveStableHttpMappings()
    {
        await using var conflictFactory = CreateFactory(new StubApplication("idempotency_conflict"));
        using var conflictClient = conflictFactory.CreateClient();
        conflictClient.AuthenticateAs("user-a");
        using var conflict = await Post(conflictClient);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        await using var unsupportedFactory = CreateFactory(new StubApplication(ProvisioningErrorCodes.HostIncompatible));
        using var unsupportedClient = unsupportedFactory.CreateClient();
        unsupportedClient.AuthenticateAs("user-a");
        using var unsupported = await Post(unsupportedClient);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
    }

    [Fact]
    public async Task ApplicationHashesOpaqueKeyAndCanonicalVersionedPayload()
    {
        var provisioning = new CapturingProvisioningService();
        var service = new ManagedGameServerApplicationService(provisioning, new StubCurrentUser());
        var result = await service.CreateAsync(new(" PALWORLD ", " My Server "), "raw-secret-key", default);
        Assert.True(result.Succeeded);
        Assert.Equal(64, provisioning.KeyHash!.Length);
        Assert.Equal(64, provisioning.Fingerprint!.Length);
        Assert.Equal("user-a", provisioning.OwnerId);
        Assert.DoesNotContain("raw-secret-key", provisioning.KeyHash);

        await service.CreateAsync(new("palworld", "My Server"), "raw-secret-key", default);
        Assert.Equal(provisioning.FirstFingerprint, provisioning.Fingerprint);
    }

    [Fact]
    public async Task MigrationUpgradesPreviousSchemaAndDatabaseRejectsDuplicateKeyHash()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gameshud-gh17-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<GamesHudDbContext>().UseSqlite($"Data Source={file}").Options;
            await using (var db = new GamesHudDbContext(options))
            {
                var migrator = db.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20260916132205_AddDurableProvisioningOrchestratorContracts");
                Assert.False(await TableExists(db, "managed_game_server_requests"));
                await migrator.MigrateAsync();
                Assert.True(await TableExists(db, "managed_game_server_requests"));

                await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
                var hash = new string('a', 64);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO managed_game_server_requests (Id, IdempotencyKeyHash, RequestFingerprint, GameServerId, ProvisioningOperationId, CreatedAtUtc) VALUES ({"1"}, {hash}, {new string('b',64)}, {"gs-1"}, {"op-1"}, {"2026-09-22T00:00:00Z"})");
                await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO managed_game_server_requests (Id, IdempotencyKeyHash, RequestFingerprint, GameServerId, ProvisioningOperationId, CreatedAtUtc) VALUES ({"2"}, {hash}, {new string('c',64)}, {"gs-2"}, {"op-2"}, {"2026-09-22T00:00:00Z"})"));
                await db.Database.CloseConnectionAsync();
            }
            SqliteConnection.ClearAllPools();
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void PublicWriteDtoCannotSetServerRuntimeOrProvisioningState()
    {
        Assert.Equal(["DisplayName", "GameId"], typeof(CreateManagedGameServerRequest).GetProperties()
            .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static async Task<bool> TableExists(GamesHudDbContext db, string name)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "$name"; parameter.Value = name;
        command.Parameters.Add(parameter); await db.Database.OpenConnectionAsync();
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client)
    {
        var csrf = await client.GetCsrfTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
        { Content = JsonContent.Create(new { gameId = "palworld", displayName = "A" }) };
        request.Headers.Add("Idempotency-Key", "key"); request.AddCsrf(csrf);
        return await client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateFactory(IManagedGameServerApplicationService application) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { TestAuthentication.Add(services); services.AddSingleton(application); services.AddSingleton<IManagedGameServerQueryService, EmptyQueries>(); }));

    private sealed class StubApplication(string? error = null) : IManagedGameServerApplicationService
    {
        public Task<CreateManagedGameServerResult> CreateAsync(CreateManagedGameServerRequest? request, string? key, CancellationToken token) =>
            Task.FromResult(string.IsNullOrEmpty(key) ? new CreateManagedGameServerResult(false, false, null, null, "invalid_idempotency_key", "Required")
                : error is null ? new CreateManagedGameServerResult(true, true, "gs-approved", "op-approved", null, null)
                : new CreateManagedGameServerResult(false, false, null, null, error, "Rejected"));
    }
    private sealed class EmptyQueries : IManagedGameServerQueryService
    {
        public Task<ManagedGameServerResponse?> GetAsync(string ownerId, string id, CancellationToken token) => Task.FromResult<ManagedGameServerResponse?>(null);
        public Task<ManagedProvisioningResponse?> GetProvisioningAsync(string ownerId, string id, CancellationToken token) => Task.FromResult<ManagedProvisioningResponse?>(null);
    }
    private sealed class CapturingProvisioningService : IGameServerProvisioningService
    {
        public string? OwnerId { get; private set; } public string? KeyHash { get; private set; } public string? Fingerprint { get; private set; } public string? FirstFingerprint { get; private set; }
        public Task<IdempotentProvisioningExecutionResult> ScheduleIdempotentProvisioningAsync(CreateGameServerProvisioningRequest request, string ownerId, string keyHash, string fingerprint, CancellationToken token)
        { OwnerId = ownerId; KeyHash = keyHash; Fingerprint = fingerprint; FirstFingerprint ??= fingerprint; return Task.FromResult(new IdempotentProvisioningExecutionResult(true, true, request.GameServerId, "op", fingerprint, "pending", null)); }
        public Task<ProvisioningPreviewResult> PreviewAsync(CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ProvisioningExecutionResult> ScheduleProvisioningAsync(CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ProvisioningExecutionResult> StartProvisioningAsync(CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class StubCurrentUser : GamesHud.Api.Authentication.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => "user-a";
    }
}
