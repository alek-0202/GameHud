using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.Tests;

public sealed class ManagedStoragePreparationTests
{
    [Fact]
    public async Task ManagedReservationCreatesDirectoryAndIsIdempotent()
    {
        using var root = new TemporaryRoot();
        var target = await BuildTarget(root.Path);
        var provider = new ManagedStorageProvider(new SystemManagedDirectoryOperations());

        var first = await provider.PrepareAsync(target, CancellationToken.None);
        var second = await provider.PrepareAsync(target, CancellationToken.None);

        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, first.Status);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, second.Status);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "servers", "server-one", "data")));
    }

    [Theory]
    [InlineData(StorageOwnerships.External)]
    [InlineData("legacy_external")]
    [InlineData("unknown")]
    public async Task NonManagedOwnershipIsRejectedBeforeMutation(string ownership)
    {
        using var root = new TemporaryRoot();
        var harness = CreateHarness(root.Path, ownership: ownership);
        var result = await harness.Builder.BuildAsync(harness.Context, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(ManagedStorageErrorCodes.OwnershipInvalid, result.SafeErrorCode);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task ReservationForAnotherServerIsRejectedBeforeMutation()
    {
        using var root = new TemporaryRoot();
        var harness = CreateHarness(root.Path, reservationServerId: "server-two");
        var result = await harness.Builder.BuildAsync(harness.Context, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("servers/server-two/data")]
    [InlineData("servers/server-one/other")]
    public async Task UnsafeOrCrossServerRelativePathIsRejected(string relativePath)
    {
        using var root = new TemporaryRoot();
        var harness = CreateHarness(root.Path, relativePath: relativePath);
        var result = await harness.Builder.BuildAsync(harness.Context, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task ReconciliationDistinguishesAbsentAndExists()
    {
        using var root = new TemporaryRoot();
        var target = await BuildTarget(root.Path);
        var provider = new ManagedStorageProvider(new SystemManagedDirectoryOperations());
        var absent = await provider.InspectAsync(target, CancellationToken.None);
        await provider.PrepareAsync(target, CancellationToken.None);
        var exists = await provider.InspectAsync(target, CancellationToken.None);
        Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent, absent.Outcome);
        Assert.Equal(ProvisioningReconciliationOutcomes.EffectExists, exists.Outcome);
    }

    [Fact]
    public async Task AmbiguousInspectionAndProviderErrorsDoNotLeakPaths()
    {
        using var root = new TemporaryRoot();
        var target = await BuildTarget(root.Path);
        var provider = new ManagedStorageProvider(new ThrowingDirectoryOperations());
        var outcome = await provider.PrepareAsync(target, CancellationToken.None);
        var inspection = await provider.InspectAsync(target, CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.KnownFailure, outcome.Status);
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, inspection.Outcome);
        Assert.DoesNotContain(root.Path, outcome.SafeMessage);
        Assert.DoesNotContain(root.Path, inspection.SafeMessage);
    }

    [Fact]
    public async Task CancellationBeforePreparationCreatesNothing()
    {
        using var root = new TemporaryRoot();
        var target = await BuildTarget(root.Path);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManagedStorageProvider(new SystemManagedDirectoryOperations()).PrepareAsync(target, source.Token));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task CompensationNeverDeletesPreparedStorage()
    {
        using var root = new TemporaryRoot();
        var harness = CreateHarness(root.Path);
        var target = (await harness.Builder.BuildAsync(harness.Context, CancellationToken.None)).Target!;
        var provider = new ManagedStorageProvider(new SystemManagedDirectoryOperations());
        await provider.PrepareAsync(target, CancellationToken.None);
        var step = new PrepareStorageProvisioningStep(harness.Builder, provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrepareStorageProvisioningStep>.Instance);
        await step.CompensateAsync(harness.Context, CancellationToken.None);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "servers", "server-one", "data")));
    }

    private static async Task<ValidatedManagedStorageTarget> BuildTarget(string root)
    {
        var harness = CreateHarness(root);
        var result = await harness.Builder.BuildAsync(harness.Context, CancellationToken.None);
        return Assert.IsType<ValidatedManagedStorageTarget>(result.Target);
    }

    private static (ManagedStorageTargetBuilder Builder, ProvisioningContext Context) CreateHarness(
        string root, string ownership = StorageOwnerships.Managed,
        string reservationServerId = "server-one", string relativePath = "servers/server-one/data")
    {
        var reservation = new StorageReservationRecord
        {
            Id = "storage-one",
            GameServerId = reservationServerId,
            StorageDefinitionId = "data",
            RelativePath = relativePath,
            Ownership = ownership,
            Status = ReservationStatuses.Reserved,
            ProvisioningOperationId = "operation-one"
        };
        var server = new ManagedGameServerRecord
        {
            Id = "server-one",
            GameId = "palworld",
            DisplayName = "Server",
            InstallationType = ManagedInstallationTypes.Managed,
            RuntimeType = "docker",
            LifecycleState = ManagedGameServerLifecycleStates.PendingProvisioning
        };
        server.StorageReservations.Add(reservation);
        var store = new FakeStore(server);
        var paths = new FakePathBuilder(root);
        var definition = new PalworldGameDefinition();
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [new("data", relativePath.Replace('\\', '/'))], [], ProvisioningStepIds.All);
        var reserved = new ManagedServerReservationResult("server-one", "operation-one", [], ["storage-one"]);
        return (new(store, paths, new GameDefinitionRegistry([definition])), new("operation-one", definition, plan, reserved));
    }

    private sealed class FakePathBuilder(string root) : IManagedStoragePathBuilder
    {
        public ManagedStorageLayout CreateLayout(GameServerId gameServerId) =>
            new(Path.GetFullPath(root), Path.Combine(root, "servers", gameServerId.ToString()), Path.Combine("servers", gameServerId.ToString()));
    }

    private sealed class FakeStore(ManagedGameServerRecord server) : IManagedServerStore
    {
        public Task<ManagedGameServerRecord?> GetManagedServerAsync(string gameServerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedGameServerRecord?>(server);
        public Task<ManagedServerReservationResult> ReserveProvisioningPlanAsync(ManagedServerProvisioningPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvisioningOperationRecord?> GetActiveOperationAsync(string gameServerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagedServerReservationConflict?> FindReservationConflictAsync(ManagedServerProvisioningPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingDirectoryOperations : IManagedDirectoryOperations
    {
        public bool Exists(string path) => throw new UnauthorizedAccessException("TEST-ONLY-RAW-PATH: " + path);
        public FileAttributes GetAttributes(string path) => throw new UnauthorizedAccessException("TEST-ONLY-RAW-PATH: " + path);
        public void Create(string path) => throw new UnauthorizedAccessException("TEST-ONLY-RAW-PATH: " + path);
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshud-gh11", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            DeleteIfEmpty(System.IO.Path.Combine(Path, "servers", "server-one", "data"));
            DeleteIfEmpty(System.IO.Path.Combine(Path, "servers", "server-one"));
            DeleteIfEmpty(System.IO.Path.Combine(Path, "servers"));
            DeleteIfEmpty(Path);
        }

        private static void DeleteIfEmpty(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path); } catch { }
        }
    }
}
