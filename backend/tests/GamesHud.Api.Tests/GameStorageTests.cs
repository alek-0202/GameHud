using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.HostCapabilities.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class GameStorageTests
{
    [Fact]
    public void ConfiguredDataRootIsUsed()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Server A", [CreateStorageDefinition()]),
            new GameServerId("server-a"));

        Assert.Equal(Path.GetFullPath(tempRoot.Path), plan.DataRoot);
    }

    [Fact]
    public void GameServerIdCreatesDeterministicDirectory()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var planner = CreatePlanner(tempRoot.Path);

        var first = planner.CreatePlan(CreateDefinition("First", [CreateStorageDefinition()]), new GameServerId("Server-One"));
        var second = planner.CreatePlan(CreateDefinition("Second", [CreateStorageDefinition()]), new GameServerId("server-one"));

        Assert.Equal(first.ServerRelativePath, second.ServerRelativePath);
        Assert.EndsWith(Path.Combine("servers", "server-one"), first.ServerRelativePath);
    }

    [Fact]
    public void DifferentServerIdsCreateDifferentDirectories()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var planner = CreatePlanner(tempRoot.Path);

        var first = planner.CreatePlan(CreateDefinition("Same", [CreateStorageDefinition()]), new GameServerId("server-one"));
        var second = planner.CreatePlan(CreateDefinition("Same", [CreateStorageDefinition()]), new GameServerId("server-two"));

        Assert.NotEqual(first.ServerRoot, second.ServerRoot);
    }

    [Fact]
    public void DisplayNameDoesNotAffectStoragePath()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var planner = CreatePlanner(tempRoot.Path);

        var first = planner.CreatePlan(CreateDefinition("Friendly Name", [CreateStorageDefinition()]), new GameServerId("stable-id"));
        var second = planner.CreatePlan(CreateDefinition("Changed Name", [CreateStorageDefinition()]), new GameServerId("stable-id"));

        Assert.Equal(first.ServerRelativePath, second.ServerRelativePath);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("/absolute")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\absolute")]
    [InlineData("name/child")]
    [InlineData("name\\child")]
    public void UnsafeGameServerIdsAreRejected(string gameServerId)
    {
        using var tempRoot = TemporaryDirectory.Create();
        var planner = CreatePlanner(tempRoot.Path);

        var exception = Assert.Throws<StoragePlanningException>(() =>
            planner.CreatePlan(CreateDefinition("Test", [CreateStorageDefinition()]), new GameServerId(gameServerId)));

        Assert.Equal(StorageIssueCodes.InvalidGameServerId, exception.Code);
    }

    [Fact]
    public void PlannedPathsRemainInsideManagedRoot()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Test", [CreateStorageDefinition()]),
            new GameServerId("safe-server"));

        Assert.StartsWith(Path.GetFullPath(tempRoot.Path), plan.ServerRoot, StringComparison.OrdinalIgnoreCase);
        Assert.All(plan.Entries, entry =>
            Assert.StartsWith("servers", entry.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingPathReturnsCollisionWithoutAdoptingData()
    {
        using var tempRoot = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(tempRoot.Path, "servers", "existing"));

        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Test", [CreateStorageDefinition()]),
            new GameServerId("existing"));

        Assert.Equal(StoragePlanStatuses.Collision, plan.Status);
        Assert.Contains(plan.Issues, issue => issue.Code == StorageIssueCodes.ExistingPath);
    }

    [Fact]
    public void ManagedOwnershipIsExplicitAndExternalOwnershipExistsForLegacyResources()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Test", [CreateStorageDefinition()]),
            new GameServerId("managed"));

        Assert.Equal(StorageOwnerships.Managed, plan.Ownership);
        Assert.Equal(StorageOwnerships.Managed, Assert.Single(plan.Entries).Ownership);
        Assert.Equal("external", StorageOwnerships.External);
    }

    [Fact]
    public void EnoughStorageReturnsReady()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(
            tempRoot.Path,
            availableBytes: 20_000).CreatePlan(
                CreateDefinition("Test", [CreateStorageDefinition(minimumBytes: 10_000)]),
                new GameServerId("ready"));

        Assert.Equal(StoragePlanStatuses.Ready, plan.Status);
        Assert.Equal((ulong)10_000, plan.RequiredBytes);
        Assert.Equal((ulong)20_000, plan.AvailableBytes);
    }

    [Fact]
    public void InsufficientStorageReturnsInsufficient()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(
            tempRoot.Path,
            availableBytes: 5_000).CreatePlan(
                CreateDefinition("Test", [CreateStorageDefinition(minimumBytes: 10_000)]),
                new GameServerId("full"));

        Assert.Equal(StoragePlanStatuses.Insufficient, plan.Status);
        Assert.Contains(plan.Issues, issue => issue.Code == StorageIssueCodes.InsufficientStorage);
    }

    [Fact]
    public void UnknownStorageReturnsUnknown()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(
            tempRoot.Path,
            availableBytes: null).CreatePlan(
                CreateDefinition("Test", [CreateStorageDefinition(minimumBytes: 10_000)]),
                new GameServerId("unknown"));

        Assert.Equal(StoragePlanStatuses.Unknown, plan.Status);
        Assert.Contains(plan.Issues, issue => issue.Code == StorageIssueCodes.StorageUnknown);
    }

    [Fact]
    public void PalworldStorageDefinitionUsesRuntimeTargetWithoutHostPath()
    {
        var definition = new PalworldGameDefinition();
        var storage = Assert.Single(definition.Storages);

        Assert.Equal("data", storage.Id);
        Assert.Equal(StoragePurposes.GameData, storage.Purpose);
        Assert.Equal("/palworld", storage.RuntimeTarget);
        Assert.True(storage.Persistent);
        Assert.True(storage.Required);
        Assert.True(storage.BackupEligible);
        Assert.True(storage.UserData);
        Assert.Null(storage.MinimumBytes);
        Assert.DoesNotContain(":", storage.RuntimeTarget);
    }

    [Fact]
    public void PlannerUsesDefinitionMetadataWithoutPalworldHardcode()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Custom", [
                CreateStorageDefinition("custom-data", "Custom data", "/srv/custom")
            ]),
            new GameServerId("custom"));

        var entry = Assert.Single(plan.Entries);
        var mount = Assert.Single(plan.Mounts);
        Assert.Equal("custom-data", entry.DefinitionId);
        Assert.Equal("/srv/custom", mount.RuntimeTarget);
    }

    [Fact]
    public void PlanningDoesNotCreateGameDirectories()
    {
        using var tempRoot = TemporaryDirectory.Create();
        var serverRoot = Path.Combine(tempRoot.Path, "servers", "not-created");

        var plan = CreatePlanner(tempRoot.Path).CreatePlan(
            CreateDefinition("Test", [CreateStorageDefinition()]),
            new GameServerId("not-created"));

        Assert.False(Directory.Exists(serverRoot));
        Assert.Equal(serverRoot, plan.ServerRoot);
    }

    [Fact]
    public async Task StoragePlanEndpointReturnsPlanForKnownGame()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/games/palworld/storage/plan",
            new GameStoragePlanRequest("palworld-preview"));
        var plan = await response.Content.ReadFromJsonAsync<GameStoragePlanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(plan);
        Assert.Equal("palworld", plan.GameId);
        Assert.Equal(StorageOwnerships.Managed, plan.Ownership);
        Assert.Contains(plan.Entries, entry => entry.RuntimeTarget == "/palworld");
    }

    [Fact]
    public async Task StoragePlanEndpointReturnsNotFoundForUnknownGame()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/games/unknown/storage/plan",
            new GameStoragePlanRequest("unknown-preview"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StoragePlanEndpointReturnsBadRequestForInvalidServerId()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/games/palworld/storage/plan",
            new GameStoragePlanRequest("../escape"));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(StorageIssueCodes.InvalidGameServerId, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoragePlanResponseDoesNotExposeSensitiveAbsolutePathsOrSecrets()
    {
        using var tempRoot = TemporaryDirectory.Create();
        await using var factory = CreateFactory(tempRoot.Path);
        using var client = factory.CreateClient();

        var plan = await client.PostAsJsonAsync(
            "/api/games/palworld/storage/plan",
            new GameStoragePlanRequest("palworld-preview"));
        var json = await plan.Content.ReadAsStringAsync();

        Assert.DoesNotContain(tempRoot.Path, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagedPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BackupPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContainerName", json, StringComparison.OrdinalIgnoreCase);
    }

    private static GameStoragePlanner CreatePlanner(
        string dataRoot,
        ulong? availableBytes = 50_000)
    {
        return new GameStoragePlanner(
            new ManagedStoragePathBuilder(Options.Create(new StorageOptions
            {
                DataRoot = dataRoot
            })),
            new FakeHostStorageInfoProvider(availableBytes));
    }

    private static GameDefinition CreateDefinition(
        string displayName,
        IReadOnlyCollection<GameStorageDefinition> storages)
    {
        return new GameDefinition(
            new GameId("test-game"),
            displayName,
            "Test game support.",
            new GameDefinitionBranding("test-game"),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview],
            storages: storages);
    }

    private static GameStorageDefinition CreateStorageDefinition(
        string id = "data",
        string label = "Game data",
        string runtimeTarget = "/data",
        ulong? minimumBytes = null)
    {
        return new GameStorageDefinition(
            id,
            label,
            StoragePurposes.GameData,
            runtimeTarget,
            persistent: true,
            required: true,
            backupEligible: true,
            userData: true,
            minimumBytes);
    }

    private static WebApplicationFactory<Program> CreateFactory(string dataRoot)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<StorageOptions>(options => options.DataRoot = dataRoot);
                    services.AddSingleton<IHostStorageInfoProvider>(
                        new FakeHostStorageInfoProvider(50_000));
                });
            });
    }

    private sealed class FakeHostStorageInfoProvider : IHostStorageInfoProvider
    {
        private readonly ulong? _availableBytes;

        public FakeHostStorageInfoProvider(ulong? availableBytes)
        {
            _availableBytes = availableBytes;
        }

        public HostStorageDriveInfo GetDriveInfo(string path)
        {
            if (_availableBytes is null)
            {
                throw new IOException("Storage unavailable.");
            }

            return new HostStorageDriveInfo(path, 100_000, _availableBytes.Value);
        }
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
                $"gameshud-storage-tests-{Guid.NewGuid():N}");

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
