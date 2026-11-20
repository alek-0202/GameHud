using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Palworld.ManagedConfiguration;

namespace GamesHud.Api.Tests;

public sealed class PalworldManagedConfigurationFileStoreTests
{
    [Fact]
    public async Task MissingFileIsWrittenAtomicallyAtFixedPalworldLocation()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget();
        var fileSystem = new TrackingFileSystem();
        var store = CreateStore(fileSystem);

        var result = await store.MaterializeAsync(target, Expected(), CancellationToken.None);

        Assert.Equal(PalworldManagedConfigurationMutationStatuses.Success, result.Status);
        Assert.True(File.Exists(target.DestinationPath));
        Assert.Equal(target.DestinationDirectory, Path.GetDirectoryName(fileSystem.CreatedTemporaryPath));
        Assert.Equal(target.DestinationDirectory, Path.GetDirectoryName(fileSystem.MoveSource));
        Assert.Equal(target.DestinationPath, fileSystem.MoveDestination);
        Assert.False(fileSystem.FinalExistedBeforeMove);
        Assert.Empty(Directory.GetFiles(target.DestinationDirectory, $"{target.TemporaryFilePrefix}*.tmp"));
    }

    [Fact]
    public async Task SemanticEqualityIsIdempotentWithoutTimestampChange()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget();
        var store = CreateStore(new TrackingFileSystem());
        await store.MaterializeAsync(target, Expected(), CancellationToken.None);
        var timestamp = File.GetLastWriteTimeUtc(target.DestinationPath);
        await Task.Delay(30);

        var second = await store.MaterializeAsync(target, Expected(), CancellationToken.None);

        Assert.Equal(PalworldManagedConfigurationMutationStatuses.Success, second.Status);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(target.DestinationPath));
    }

    [Fact]
    public async Task SemanticallyEqualNonCanonicalFileIsNotRewritten()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget(createDestinationDirectory: true);
        var content = "[/Script/Pal.PalGameWorldSettings]\r\n" +
            "OptionSettings=(Difficulty=Difficult, ServerName=\"Server\", ServerDescription=\"Description\", ServerPlayerMaxNum=32, ServerPassword=\"server-secret\", AdminPassword=\"admin-secret\")\r\n";
        await File.WriteAllTextAsync(target.DestinationPath, content);
        var timestamp = File.GetLastWriteTimeUtc(target.DestinationPath);

        var result = await CreateStore(new TrackingFileSystem()).MaterializeAsync(
            target, Expected(), CancellationToken.None);

        Assert.Equal(PalworldManagedConfigurationMutationStatuses.Success, result.Status);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(target.DestinationPath));
        Assert.Equal(content, await File.ReadAllTextAsync(target.DestinationPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDriftOrInvalidContentIsNeverOverwritten(bool validDrift)
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget(createDestinationDirectory: true);
        var original = validDrift
            ? new PalworldManagedConfigurationSerializer().Serialize(Expected() with { MaxPlayers = 4 })
            : "malformed sentinel";
        await File.WriteAllTextAsync(target.DestinationPath, original);
        var store = CreateStore(new TrackingFileSystem());

        var result = await store.MaterializeAsync(target, Expected(), CancellationToken.None);
        var inspection = await store.InspectAsync(target, Expected(), CancellationToken.None);

        Assert.Equal(PalworldManagedConfigurationMutationStatuses.KnownFailure, result.Status);
        Assert.Equal(validDrift ? PalworldManagedConfigurationErrorCodes.DriftDetected
            : PalworldManagedConfigurationErrorCodes.ExistingInvalid, result.SafeErrorCode);
        Assert.Equal(original, await File.ReadAllTextAsync(target.DestinationPath));
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, inspection.Outcome);
    }

    [Fact]
    public async Task ReconciliationCoversCrashWindowsBeforeTempTempAndFinal()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget(createDestinationDirectory: true);
        var store = CreateStore(new TrackingFileSystem());

        Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent,
            (await store.InspectAsync(target, Expected(), CancellationToken.None)).Outcome);

        var temp = Path.Combine(target.DestinationDirectory,
            $"{target.TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temp, "partial");
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await store.InspectAsync(target, Expected(), CancellationToken.None)).Outcome);

        File.Delete(temp);
        await File.WriteAllTextAsync(target.DestinationPath,
            new PalworldManagedConfigurationSerializer().Serialize(Expected()));
        Assert.Equal(ProvisioningReconciliationOutcomes.EffectExists,
            (await store.InspectAsync(target, Expected(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task FailedTempWriteIsAbsentOnlyWhenCleanupIsProven()
    {
        using var cleanRoot = new TemporaryRoot();
        var cleanTarget = cleanRoot.CreateTarget();
        var cleaned = await CreateStore(new TrackingFileSystem { ThrowOnFlushToDisk = true })
            .MaterializeAsync(cleanTarget, Expected(), CancellationToken.None);
        Assert.Equal(PalworldManagedConfigurationMutationStatuses.KnownFailure, cleaned.Status);
        Assert.False(File.Exists(cleanTarget.DestinationPath));
        Assert.Empty(Directory.GetFiles(cleanTarget.DestinationDirectory, $"{cleanTarget.TemporaryFilePrefix}*.tmp"));

        using var ambiguousRoot = new TemporaryRoot();
        var ambiguousTarget = ambiguousRoot.CreateTarget();
        var ambiguous = await CreateStore(new TrackingFileSystem { ThrowOnFlushToDisk = true, ThrowOnDelete = true })
            .MaterializeAsync(ambiguousTarget, Expected(), CancellationToken.None);
        Assert.Equal(PalworldManagedConfigurationMutationStatuses.Unknown, ambiguous.Status);
        Assert.Single(Directory.GetFiles(ambiguousTarget.DestinationDirectory,
            $"{ambiguousTarget.TemporaryFilePrefix}*.tmp"));
    }

    [Fact]
    public async Task UnrelatedTempNameDoesNotCreateFalsePositive()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget(createDestinationDirectory: true);
        await File.WriteAllTextAsync(Path.Combine(target.DestinationDirectory,
            $"{target.TemporaryFilePrefix}not-an-opaque-id.tmp"), "third-party");

        var result = await CreateStore(new TrackingFileSystem()).InspectAsync(
            target, Expected(), CancellationToken.None);

        Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent, result.Outcome);
    }

    [Fact]
    public async Task TraversalAbsoluteEscapeAndReparseStateFailClosed()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget(createDestinationDirectory: true);
        var escaped = target with { DestinationPath = Path.Combine(root.ParentPath, "escaped.ini") };
        var store = CreateStore(new TrackingFileSystem());
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await store.InspectAsync(escaped, Expected(), CancellationToken.None)).Outcome);

        var reparse = new TrackingFileSystem { ReparsePath = target.StorageRoot };
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await CreateStore(reparse).InspectAsync(target, Expected(), CancellationToken.None)).Outcome);

        await File.WriteAllTextAsync(target.DestinationPath,
            new PalworldManagedConfigurationSerializer().Serialize(Expected()));
        var finalReparse = new TrackingFileSystem { ReparsePath = target.DestinationPath };
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await CreateStore(finalReparse).InspectAsync(target, Expected(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task FinalFileChangedAfterSuccessBecomesAmbiguous()
    {
        using var root = new TemporaryRoot();
        var target = root.CreateTarget();
        var store = CreateStore(new TrackingFileSystem());
        await store.MaterializeAsync(target, Expected(), CancellationToken.None);
        await File.WriteAllTextAsync(target.DestinationPath,
            new PalworldManagedConfigurationSerializer().Serialize(Expected() with { ServerName = "Drift" }));
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            (await store.InspectAsync(target, Expected(), CancellationToken.None)).Outcome);
    }

    private static PalworldManagedConfigurationFileStore CreateStore(IPalworldManagedConfigurationFileSystem fs) =>
        new(fs, new PalworldManagedConfigurationSerializer());

    private static PalworldManagedConfiguration Expected() =>
        new("Server", "Description", 32, "Hard", "server-secret", "admin-secret");

    private sealed class TrackingFileSystem : IPalworldManagedConfigurationFileSystem
    {
        private readonly SystemPalworldManagedConfigurationFileSystem _inner = new();
        public bool ThrowOnFlushToDisk { get; init; }
        public bool ThrowOnDelete { get; init; }
        public string? ReparsePath { get; init; }
        public string? CreatedTemporaryPath { get; private set; }
        public string? MoveSource { get; private set; }
        public string? MoveDestination { get; private set; }
        public bool FinalExistedBeforeMove { get; private set; }

        public FileAttributes? GetAttributesOrNull(string path)
        {
            var attributes = _inner.GetAttributesOrNull(path);
            return attributes is not null && ReparsePath is not null
                && Path.GetFullPath(path).Equals(Path.GetFullPath(ReparsePath), StringComparison.OrdinalIgnoreCase)
                ? attributes | FileAttributes.ReparsePoint
                : attributes;
        }
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IReadOnlyCollection<string> EnumerateEntries(string directory, string searchPattern) =>
            _inner.EnumerateEntries(directory, searchPattern);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream CreateNew(string path) { CreatedTemporaryPath = path; return _inner.CreateNew(path); }
        public void FlushToDisk(Stream stream)
        {
            if (ThrowOnFlushToDisk) throw new IOException("test-only write failure");
            _inner.FlushToDisk(stream);
        }
        public void MoveNoReplace(string source, string destination)
        {
            MoveSource = source;
            MoveDestination = destination;
            FinalExistedBeforeMove = File.Exists(destination);
            _inner.MoveNoReplace(source, destination);
        }
        public void Delete(string path)
        {
            if (ThrowOnDelete) throw new IOException("test-only cleanup failure");
            _inner.Delete(path);
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            ParentPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshud-gh14-tests");
            Path = System.IO.Path.Combine(ParentPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "servers", "server-one", "data"));
        }
        public string ParentPath { get; }
        public string Path { get; }
        public PalworldManagedConfigurationTarget CreateTarget(bool createDestinationDirectory = false)
        {
            var storage = System.IO.Path.Combine(Path, "servers", "server-one", "data");
            var directory = System.IO.Path.Combine(storage, "Pal", "Saved", "Config", "LinuxServer");
            if (createDestinationDirectory) Directory.CreateDirectory(directory);
            return new(new("server-one"), "operation-one", Path, storage, directory,
                System.IO.Path.Combine(directory, "PalWorldSettings.ini"),
                ".PalWorldSettings.ini.gameshud-operation-one-");
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
