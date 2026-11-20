using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Secrets.Configuration;
using GamesHud.Api.Secrets.Models;
using GamesHud.Api.Secrets.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public void SecretIdCreatesOpaqueUniqueGuidValues()
    {
        var first = SecretId.New();
        var second = SecretId.New();

        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParseExact(first.Value, "N", out _));
        Assert.True(Guid.TryParseExact(second.Value, "N", out _));
    }

    [Fact]
    public async Task StoreRoundTripsSecretWithoutPlaintextOnDisk()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        const string knownSecret = "roundtrip-secret-value";

        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.GameServerPassword),
            SecretValue.FromPlainText(knownSecret));

        var value = await store.GetAsync(reference);
        var storedBytes = ReadAllStoredBytes(tempRoot.Path);

        Assert.Equal(knownSecret, value.Reveal());
        Assert.DoesNotContain(knownSecret, storedBytes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplacePreservesSecretReferenceAndUpdatesMaterial()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.IntegrationToken),
            SecretValue.FromPlainText("old-secret-value"));

        await store.ReplaceAsync(reference, SecretValue.FromPlainText("new-secret-value"));

        var value = await store.GetAsync(reference);

        Assert.Equal("new-secret-value", value.Reveal());
        Assert.DoesNotContain("old-secret-value", ReadAllStoredBytes(tempRoot.Path), StringComparison.Ordinal);
        Assert.DoesNotContain("new-secret-value", ReadAllStoredBytes(tempRoot.Path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteRemovesSecretMaterialAndMissingSecretFailsClearly()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.Webhook),
            SecretValue.FromPlainText("delete-secret-value"));

        await store.DeleteAsync(reference);

        await Assert.ThrowsAsync<SecretNotFoundException>(() => store.GetAsync(reference));
        await Assert.ThrowsAsync<SecretNotFoundException>(() => store.DeleteAsync(reference));
    }

    [Fact]
    public async Task CorruptedSecretFailsClosedWithoutReturningPartialPlaintext()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        const string knownSecret = "corrupted-secret-value";
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.GameAdminPassword),
            SecretValue.FromPlainText(knownSecret));

        await File.WriteAllTextAsync(GetSingleSecretFile(tempRoot.Path), "{ \"ciphertext\": \"broken\" }");

        var exception = await Assert.ThrowsAsync<SecretStoreCorruptedException>(() => store.GetAsync(reference));

        Assert.DoesNotContain(knownSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongMasterKeyFailsClosed()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        var wrongKeyStore = CreateStore(tempRoot.Path, CreateMasterKey());
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.GameRestCredential),
            SecretValue.FromPlainText("wrong-key-secret-value"));

        await Assert.ThrowsAsync<SecretStoreCorruptedException>(() => wrongKeyStore.GetAsync(reference));
    }

    [Fact]
    public void SecretValueToStringAndJsonSerializationDoNotRevealPlaintext()
    {
        const string knownSecret = "serialization-secret-value";
        var value = SecretValue.FromPlainText(knownSecret);

        var json = JsonSerializer.Serialize(value);

        Assert.DoesNotContain(knownSecret, value.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(knownSecret, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointReturnsHealthWithoutSecretMaterialPathsCountsOrIds()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var masterKey = CreateMasterKey();
        await using var factory = CreateFactory(tempRoot.Path, masterKey);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            await store.StoreAsync(
                SecretPurpose.From(SecretPurpose.Webhook),
                SecretValue.FromPlainText("endpoint-secret-value"));
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/system/secrets");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("endpoint-secret-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain(tempRoot.Path, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secrets", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".json", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreWithoutMasterKeyFailsClosedWithoutPlaintextFallback()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, string.Empty);

        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() =>
            store.StoreAsync(
                SecretPurpose.From(SecretPurpose.IntegrationToken),
                SecretValue.FromPlainText("no-key-secret-value")));

        Assert.False(Directory.Exists(Path.Combine(tempRoot.Path, "system", "secrets")));
    }

    [Fact]
    public async Task PathsStayInsideManagedSystemRoot()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey(), out var layout);

        await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.GameServerPassword),
            SecretValue.FromPlainText("path-secret-value"));

        Assert.Equal(Path.Combine(tempRoot.Path, "system", "secrets"), layout.SecretsRoot);
        Assert.StartsWith(Path.GetFullPath(tempRoot.Path), layout.SecretsRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentReplaceDoesNotCorruptStoredSecret()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.IntegrationToken),
            SecretValue.FromPlainText("initial-concurrent-secret"));
        var replacements = Enumerable.Range(0, 20)
            .Select(index => $"replacement-secret-{index}")
            .ToArray();

        await Task.WhenAll(replacements.Select(secret =>
            store.ReplaceAsync(reference, SecretValue.FromPlainText(secret))));

        var value = await store.GetAsync(reference);

        Assert.Contains(value.Reveal(), replacements);
    }

    [Fact]
    public async Task AtomicWritesDoNotLeavePlaintextTempFiles()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        const string knownSecret = "atomic-secret-value";
        var reference = await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.Webhook),
            SecretValue.FromPlainText(knownSecret));

        await store.ReplaceAsync(reference, SecretValue.FromPlainText("atomic-secret-value-updated"));

        var files = Directory.GetFiles(Path.Combine(tempRoot.Path, "system", "secrets"), "*", SearchOption.AllDirectories);

        Assert.DoesNotContain(files, file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(knownSecret, ReadAllStoredBytes(tempRoot.Path), StringComparison.Ordinal);
        Assert.DoesNotContain("atomic-secret-value-updated", ReadAllStoredBytes(tempRoot.Path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretStoreInitializationDoesNotAutoImportLegacyPalworldSecrets()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path, CreateMasterKey());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(tempRoot.Path, "system", "secrets")));
    }

    [Fact]
    public async Task SqliteDatabaseDoesNotContainStoredSecretMaterial()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var dbContext = CreateDbContext(tempRoot.Path, out var layout);
        await CreateInitializer(dbContext, tempRoot.Path).InitializeAsync();
        var store = CreateStore(tempRoot.Path, CreateMasterKey());
        const string knownSecret = "database-secret-value";

        await store.StoreAsync(
            SecretPurpose.From(SecretPurpose.GameRestCredential),
            SecretValue.FromPlainText(knownSecret));
        var databaseBytes = await File.ReadAllBytesAsync(layout.DatabasePath);

        Assert.DoesNotContain(knownSecret, Encoding.UTF8.GetString(databaseBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppsettingsDoesNotContainProductionMasterKey()
    {
        var appsettings = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json"));

        using var document = JsonDocument.Parse(appsettings);
        var masterKey = document.RootElement
            .GetProperty("Secrets")
            .GetProperty("MasterKey")
            .GetString();

        Assert.True(string.IsNullOrEmpty(masterKey));
    }

    private static ISecretStore CreateStore(string dataRoot, string masterKey)
    {
        return CreateStore(dataRoot, masterKey, out _);
    }

    private static ISecretStore CreateStore(string dataRoot, string masterKey, out SecretStoreLayout layout)
    {
        var persistenceLayoutResolver = new PersistenceLayoutResolver(Options.Create(new StorageOptions
        {
            DataRoot = dataRoot,
        }));
        var layoutResolver = new SecretStoreLayoutResolver(persistenceLayoutResolver);
        layout = layoutResolver.ResolveLayout();
        var keyProvider = new ConfigurationSecretKeyProvider(Options.Create(new SecretStorageOptions
        {
            MasterKey = masterKey,
        }));

        return new FileSecretStore(
            layoutResolver,
            new AesGcmSecretProtector(keyProvider),
            TimeProvider.System);
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

    private static WebApplicationFactory<Program> CreateFactory(string dataRoot, string masterKey)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<StorageOptions>(options => options.DataRoot = dataRoot);
                    services.Configure<SecretStorageOptions>(options => options.MasterKey = masterKey);
                });
            });
    }

    private static string CreateMasterKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static string ReadAllStoredBytes(string dataRoot)
    {
        var systemRoot = Path.Combine(dataRoot, "system");
        if (!Directory.Exists(systemRoot))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var file in Directory.GetFiles(systemRoot, "*", SearchOption.AllDirectories))
        {
            builder.Append(File.ReadAllText(file));
        }

        return builder.ToString();
    }

    private static string GetSingleSecretFile(string dataRoot)
    {
        return Assert.Single(Directory.GetFiles(
            Path.Combine(dataRoot, "system", "secrets"),
            "*.json",
            SearchOption.TopDirectoryOnly));
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
                $"gameshud-secret-tests-{Guid.NewGuid():N}");

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
