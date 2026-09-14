using System.Text;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.Palworld.ManagedConfiguration;

public interface IPalworldManagedConfigurationFileStore
{
    Task<PalworldManagedConfigurationMutationResult> MaterializeAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        CancellationToken cancellationToken);
    Task<ProvisioningReconciliationResult> InspectAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        CancellationToken cancellationToken);
}

public sealed class PalworldManagedConfigurationFileStore : IPalworldManagedConfigurationFileStore
{
    private readonly IPalworldManagedConfigurationFileSystem _fileSystem;
    private readonly IPalworldManagedConfigurationSerializer _serializer;

    public PalworldManagedConfigurationFileStore(
        IPalworldManagedConfigurationFileSystem fileSystem,
        IPalworldManagedConfigurationSerializer serializer)
    {
        _fileSystem = fileSystem;
        _serializer = serializer;
    }

    public async Task<PalworldManagedConfigurationMutationResult> MaterializeAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        string? temporaryPath = null;
        var mutationStarted = false;
        try
        {
            var content = _serializer.Serialize(expected);
            var before = await InspectCoreAsync(target, expected, allowMissingDirectories: true, cancellationToken);
            if (before.Outcome == ProvisioningReconciliationOutcomes.EffectExists)
                return PalworldManagedConfigurationMutationResult.Success();
            if (before.Outcome == ProvisioningReconciliationOutcomes.Ambiguous)
                return ExistingFailure(target, expected);

            PrepareDestinationDirectories(target);
            EnsureSafeState(target, requireDestinationDirectory: true);
            if (FindRelevantTemporaryFiles(target).Count != 0) return PalworldManagedConfigurationMutationResult.Unknown();
            if (_fileSystem.GetAttributesOrNull(target.DestinationPath) is not null)
                return ExistingFailure(target, expected);

            temporaryPath = Path.Combine(target.DestinationDirectory,
                $"{target.TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
            temporaryPath = PalworldManagedPathSafety.EnsureContained(target.DestinationDirectory, temporaryPath);
            mutationStarted = true;
            await using (var stream = _fileSystem.CreateNew(temporaryPath))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                _fileSystem.FlushToDisk(stream);
            }

            EnsureSafeState(target, requireDestinationDirectory: true);
            EnsureRegularFile(temporaryPath);
            if (_fileSystem.GetAttributesOrNull(target.DestinationPath) is not null)
                throw new IOException("Destination changed during managed configuration materialization.");
            _fileSystem.MoveNoReplace(temporaryPath, target.DestinationPath);
            temporaryPath = null;

            var after = await InspectCoreAsync(target, expected, allowMissingDirectories: false, cancellationToken);
            return after.Outcome == ProvisioningReconciliationOutcomes.EffectExists
                ? PalworldManagedConfigurationMutationResult.Success()
                : PalworldManagedConfigurationMutationResult.Unknown();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupTemporaryFile(target, temporaryPath);
            throw;
        }
        catch (PalworldManagedConfigurationException exception)
        {
            CleanupTemporaryFile(target, temporaryPath);
            return PalworldManagedConfigurationMutationResult.KnownFailure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            CleanupTemporaryFile(target, temporaryPath);
            if (!mutationStarted)
                return PalworldManagedConfigurationMutationResult.KnownFailure(
                    PalworldManagedConfigurationErrorCodes.WriteFailed,
                    "Managed Palworld configuration could not be written.");
            var inspection = await InspectAfterFailureAsync(target, expected, cancellationToken);
            return inspection.Outcome == ProvisioningReconciliationOutcomes.EffectAbsent
                ? PalworldManagedConfigurationMutationResult.KnownFailure(
                    PalworldManagedConfigurationErrorCodes.WriteFailed,
                    "Managed Palworld configuration could not be written.")
                : PalworldManagedConfigurationMutationResult.Unknown();
        }
    }

    public Task<ProvisioningReconciliationResult> InspectAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        CancellationToken cancellationToken) =>
        InspectCoreAsync(target, expected, allowMissingDirectories: true, cancellationToken);

    private async Task<ProvisioningReconciliationResult> InspectCoreAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        bool allowMissingDirectories,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeState(target, requireDestinationDirectory: !allowMissingDirectories);
            var destinationDirectoryAttributes = _fileSystem.GetAttributesOrNull(target.DestinationDirectory);
            if (destinationDirectoryAttributes is null)
                return Absent();
            if (FindRelevantTemporaryFiles(target).Count != 0) return Ambiguous();

            var attributes = _fileSystem.GetAttributesOrNull(target.DestinationPath);
            if (attributes is null) return Absent();
            EnsureRegularFile(target.DestinationPath);
            await using var stream = _fileSystem.OpenRead(target.DestinationPath);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: false);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return _serializer.TryParse(content, out var actual) && actual == expected ? Exists() : Ambiguous();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or PathTooLongException
            or DecoderFallbackException or StoragePlanningException or PalworldManagedConfigurationException)
        {
            return Ambiguous();
        }
    }

    private async Task<ProvisioningReconciliationResult> InspectAfterFailureAsync(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected,
        CancellationToken cancellationToken)
    {
        try { return await InspectCoreAsync(target, expected, allowMissingDirectories: true, cancellationToken); }
        catch (OperationCanceledException) { return Ambiguous(); }
    }

    private PalworldManagedConfigurationMutationResult ExistingFailure(
        PalworldManagedConfigurationTarget target,
        PalworldManagedConfiguration expected)
    {
        try
        {
            var attributes = _fileSystem.GetAttributesOrNull(target.DestinationPath);
            if (attributes is null || IsUnsafe(attributes.Value)) return PalworldManagedConfigurationMutationResult.Unknown();
            using var stream = _fileSystem.OpenRead(target.DestinationPath);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: false);
            var content = reader.ReadToEnd();
            return _serializer.TryParse(content, out var actual)
                ? actual == expected
                    ? PalworldManagedConfigurationMutationResult.Unknown()
                    : PalworldManagedConfigurationMutationResult.KnownFailure(
                    PalworldManagedConfigurationErrorCodes.DriftDetected,
                    "Managed Palworld configuration differs from durable intent.")
                : PalworldManagedConfigurationMutationResult.KnownFailure(
                    PalworldManagedConfigurationErrorCodes.ExistingInvalid,
                    "Existing managed Palworld configuration is invalid.");
        }
        catch { return PalworldManagedConfigurationMutationResult.Unknown(); }
    }

    private void PrepareDestinationDirectories(PalworldManagedConfigurationTarget target)
    {
        EnsureSafeState(target, requireDestinationDirectory: false);
        var relative = Path.GetRelativePath(target.StorageRoot, target.DestinationDirectory);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)) throw Unsafe();
        var current = target.StorageRoot;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = PalworldManagedPathSafety.EnsureContained(target.StorageRoot, Path.Combine(current, segment));
            var attributes = _fileSystem.GetAttributesOrNull(current);
            if (attributes is null) _fileSystem.CreateDirectory(current);
            EnsureDirectory(current);
        }
        EnsureSafeState(target, requireDestinationDirectory: true);
    }

    private void EnsureSafeState(PalworldManagedConfigurationTarget target, bool requireDestinationDirectory)
    {
        var storage = PalworldManagedPathSafety.EnsureContained(target.DataRoot, target.StorageRoot);
        var destination = PalworldManagedPathSafety.EnsureContained(storage, target.DestinationPath);
        if (destination != Path.GetFullPath(target.DestinationPath)) throw Unsafe();
        EnsureDirectory(target.DataRoot);
        EnsureDirectory(target.StorageRoot);

        var current = new DirectoryInfo(target.DestinationDirectory);
        var dataRoot = Path.GetFullPath(target.DataRoot);
        while (true)
        {
            var attributes = _fileSystem.GetAttributesOrNull(current.FullName);
            if (attributes is not null && (IsUnsafe(attributes.Value)
                || (attributes.Value & FileAttributes.Directory) == 0)) throw Unsafe();
            if (current.FullName.Equals(dataRoot, PalworldManagedPathSafety.PathComparison)) break;
            current = current.Parent ?? throw Unsafe();
        }
        if (requireDestinationDirectory) EnsureDirectory(target.DestinationDirectory);
        var finalAttributes = _fileSystem.GetAttributesOrNull(target.DestinationPath);
        if (finalAttributes is not null && IsUnsafe(finalAttributes.Value)) throw Unsafe();
    }

    private IReadOnlyCollection<string> FindRelevantTemporaryFiles(PalworldManagedConfigurationTarget target)
    {
        if (_fileSystem.GetAttributesOrNull(target.DestinationDirectory) is null) return [];
        var pattern = $"{target.TemporaryFilePrefix}*.tmp";
        return _fileSystem.EnumerateEntries(target.DestinationDirectory, pattern)
            .Where(path => IsControlledTemporaryName(target, path)).ToArray();
    }

    private static bool IsControlledTemporaryName(PalworldManagedConfigurationTarget target, string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(target.TemporaryFilePrefix, StringComparison.Ordinal)
            || !name.EndsWith(".tmp", StringComparison.Ordinal)) return false;
        var opaque = name[target.TemporaryFilePrefix.Length..^4];
        return opaque.Length == 32 && opaque.All(char.IsAsciiHexDigit)
            && Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(
                Path.GetFullPath(target.DestinationDirectory), PalworldManagedPathSafety.PathComparison);
    }

    private void CleanupTemporaryFile(PalworldManagedConfigurationTarget target, string? path)
    {
        if (path is null || !IsControlledTemporaryName(target, path)) return;
        try
        {
            var attributes = _fileSystem.GetAttributesOrNull(path);
            if (attributes is not null && !IsUnsafe(attributes.Value)) _fileSystem.Delete(path);
        }
        catch { }
    }

    private void EnsureDirectory(string path)
    {
        var attributes = _fileSystem.GetAttributesOrNull(path);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) == 0 || IsUnsafe(attributes.Value)) throw Unsafe();
    }

    private void EnsureRegularFile(string path)
    {
        var attributes = _fileSystem.GetAttributesOrNull(path);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) != 0 || IsUnsafe(attributes.Value)) throw Unsafe();
    }

    private static bool IsUnsafe(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) != 0;
    private static PalworldManagedConfigurationException Unsafe() => new(
        PalworldManagedConfigurationErrorCodes.TargetInvalid,
        "Managed Palworld configuration filesystem state is unsafe.");
    private static ProvisioningReconciliationResult Exists() => new(
        ProvisioningReconciliationOutcomes.EffectExists, "Managed Palworld configuration matches durable intent.");
    private static ProvisioningReconciliationResult Absent() => new(
        ProvisioningReconciliationOutcomes.EffectAbsent, "Managed Palworld configuration is absent.");
    private static ProvisioningReconciliationResult Ambiguous() => new(
        ProvisioningReconciliationOutcomes.Ambiguous, "Managed Palworld configuration state could not be proven safely.");
}
