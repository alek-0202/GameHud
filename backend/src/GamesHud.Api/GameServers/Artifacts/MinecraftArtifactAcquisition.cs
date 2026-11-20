using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.ManagedServers;

namespace GamesHud.Api.GameServers.Artifacts;

public static class MinecraftArtifactErrorCodes
{
    public const string DownloadFailed = "minecraft_artifact_download_failed";
    public const string IdentityMismatch = "minecraft_artifact_identity_mismatch";
    public const string ArchiveUnsafe = "minecraft_artifact_archive_unsafe";
    public const string TargetConflict = "minecraft_artifact_target_conflict";
    public const string PublicationFailed = "minecraft_artifact_publication_failed";
}

public sealed record MinecraftArtifactAcquisitionResult(
    bool Succeeded,
    string? SafeCode = null,
    string? SafeMessage = null,
    bool OutcomeUnknown = false);

public interface IMinecraftProvisioningIntentReader
{
    Task<MinecraftInitialProvisioningConfiguration> ReadAsync(
        GameServerId gameServerId,
        CancellationToken cancellationToken);
}

public sealed class MinecraftProvisioningIntentReader(
    IGameProvisioningConfigurationStore configurations,
    MinecraftProvisioningConfigurationCodec codec) : IMinecraftProvisioningIntentReader
{
    public async Task<MinecraftInitialProvisioningConfiguration> ReadAsync(
        GameServerId gameServerId,
        CancellationToken cancellationToken)
    {
        var durable = await configurations.LoadAsync(gameServerId.ToString(), codec, cancellationToken);
        if (durable.TypedValue is not MinecraftInitialProvisioningConfiguration intent)
            throw new GameConfigurationException(
                GameConfigurationErrorCodes.Invalid,
                "Managed Minecraft configuration intent is invalid.");
        return intent;
    }
}

public interface IMinecraftArtifactAcquisitionService
{
    Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
        string targetRoot,
        string operationId,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken);

    Task<ProvisioningReconciliationResult> InspectAsync(
        string targetRoot,
        string operationId,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken);
}

public sealed class MinecraftArtifactAcquisitionService(HttpClient httpClient)
    : IMinecraftArtifactAcquisitionService
{
    internal const long MaximumArchiveBytes = 800L * 1024 * 1024;
    internal const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaximumEntryBytes = 256L * 1024 * 1024;
    internal const int MaximumEntries = 5_000;
    internal const int MaximumCompressionRatio = 200;
    internal const string MarkerFileName = ".gameshud-lunchtime-artifact.json";

    private static readonly string[] AllowedDirectories =
    [
        "config", "datapacks", "defaultconfigs", "kubejs", "modernfix", "mods", "schematics"
    ];
    private static readonly string[] AllowedRootFiles = ["server-icon.png", "server.properties"];
    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web);

    public async Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
        string targetRoot,
        string operationId,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken)
    {
        try
        {
            var paths = ResolvePaths(targetRoot, operationId);
            var targetState = InspectPublished(paths.Target, intent);
            if (targetState == PublishedState.Valid) return new(true);
            if (targetState == PublishedState.Conflict)
                return Failed(MinecraftArtifactErrorCodes.TargetConflict,
                    "Managed Minecraft storage contains unexpected files.");

            if (InspectPublished(paths.Staging, intent) != PublishedState.Valid)
            {
                ResetStaging(paths);
                Directory.CreateDirectory(paths.Staging);
                await DownloadAsync(paths.Archive, intent, cancellationToken);
                var extraction = await ExtractAsync(paths.Archive, paths.Staging, intent, cancellationToken);
                await WriteMarkerAsync(paths.Staging, intent, extraction, cancellationToken);
            }

            if (Directory.Exists(paths.Target))
            {
                if (Directory.EnumerateFileSystemEntries(paths.Target).Any())
                    return Failed(MinecraftArtifactErrorCodes.TargetConflict,
                        "Managed Minecraft storage contains unexpected files.");
                Directory.Delete(paths.Target);
            }

            Directory.Move(paths.Staging, paths.Target);
            if (File.Exists(paths.Archive)) File.Delete(paths.Archive);
            return InspectPublished(paths.Target, intent) == PublishedState.Valid
                ? new(true)
                : Failed(MinecraftArtifactErrorCodes.PublicationFailed,
                    "Minecraft server files could not be published safely.", outcomeUnknown: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MinecraftArtifactException exception)
        {
            return Failed(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(MinecraftArtifactErrorCodes.PublicationFailed,
                "Minecraft server file publication outcome is unknown.", outcomeUnknown: true);
        }
    }

    public Task<ProvisioningReconciliationResult> InspectAsync(
        string targetRoot,
        string operationId,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var paths = ResolvePaths(targetRoot, operationId);
            var target = InspectPublished(paths.Target, intent);
            if (target == PublishedState.Valid)
                return Task.FromResult(new ProvisioningReconciliationResult(
                    ProvisioningReconciliationOutcomes.EffectExists,
                    "The approved Minecraft artifact is published."));
            if (target == PublishedState.EmptyOrAbsent)
                return Task.FromResult(new ProvisioningReconciliationResult(
                    ProvisioningReconciliationOutcomes.EffectAbsent,
                    "The approved Minecraft artifact is not published."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or JsonException or MinecraftArtifactException)
        {
        }

        return Task.FromResult(new ProvisioningReconciliationResult(
            ProvisioningReconciliationOutcomes.Ambiguous,
            "Minecraft artifact state could not be proven safely."));
    }

    private async Task DownloadAsync(
        string archivePath,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken)
    {
        if (intent.ArtifactSizeBytes <= 0 || intent.ArtifactSizeBytes > MaximumArchiveBytes)
            throw Unsafe("The approved Minecraft artifact exceeds the download limit.");

        using var request = new HttpRequestMessage(HttpMethod.Get, intent.ArtifactUrl);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode
            || response.RequestMessage?.RequestUri is not { Scheme: "https", Host: "mediafilez.forgecdn.net" }
            || response.Content.Headers.ContentLength != intent.ArtifactSizeBytes)
            throw new MinecraftArtifactException(
                MinecraftArtifactErrorCodes.DownloadFailed,
                "The approved Minecraft artifact could not be downloaded.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumArchiveBytes || total > intent.ArtifactSizeBytes)
                throw Unsafe("The approved Minecraft artifact exceeds the download limit.");
            hasher.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);

        var digest = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        if (total != intent.ArtifactSizeBytes || digest != intent.ArtifactSha256)
            throw new MinecraftArtifactException(
                MinecraftArtifactErrorCodes.IdentityMismatch,
                "The downloaded Minecraft artifact did not match its approved identity.");
    }

    private static async Task<ExtractionSummary> ExtractAsync(
        string archivePath,
        string stagingRoot,
        MinecraftInitialProvisioningConfiguration intent,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntries)
            throw Unsafe("The Minecraft artifact contains too many files.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        var extractedEntries = 0;
        var extractedBytes = 0L;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntry(entry, intent, seen);
            var relative = entry.FullName[(intent.ArtifactRoot.Length + 1)..];
            if (string.IsNullOrEmpty(entry.Name))
            {
                var directory = relative.TrimEnd('/');
                if (directory.Length > 0 && ShouldExtract($"{directory}/placeholder"))
                    Directory.CreateDirectory(EnsureContained(stagingRoot, directory));
                continue;
            }
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw Unsafe("The Minecraft artifact expands beyond the approved limit.");

            if (!ShouldExtract(relative)) continue;
            var destinationPath = EnsureContained(stagingRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var copied = await CopyBoundedAsync(source, destination, entry.Length, cancellationToken);
            if (copied != entry.Length) throw Unsafe("A Minecraft artifact entry size did not match its ZIP metadata.");
            extractedEntries++;
            extractedBytes = checked(extractedBytes + copied);
        }

        if (extractedEntries == 0
            || !AllowedDirectories.All(directory => Directory.Exists(Path.Combine(stagingRoot, directory)))
            || !File.Exists(Path.Combine(stagingRoot, "server.properties")))
            throw Unsafe("The Minecraft artifact layout does not match the approved server pack.");
        return new(extractedEntries, extractedBytes);
    }

    private static void ValidateEntry(
        ZipArchiveEntry entry,
        MinecraftInitialProvisioningConfiguration intent,
        HashSet<string> seen)
    {
        var path = entry.FullName;
        var expectedPrefix = $"{intent.ArtifactRoot}/";
        var relativePath = path.StartsWith(expectedPrefix, StringComparison.Ordinal)
            ? path[expectedPrefix.Length..]
            : string.Empty;
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\')
            || path.StartsWith('/')
            || Path.IsPathFullyQualified(path)
            || path.Contains(':')
            || path.Split('/').Any(segment => segment is "." or "..")
            || relativePath.StartsWith('/')
            || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
            || !seen.Add(path.TrimEnd('/')))
            throw Unsafe("The Minecraft artifact contains an unsafe or duplicate path.");
        if (entry.Length < 0 || entry.Length > MaximumEntryBytes
            || entry.CompressedLength < 0
            || entry.Length > 0 && entry.CompressedLength == 0
            || entry.CompressedLength > 0
                && entry.Length > entry.CompressedLength * MaximumCompressionRatio)
            throw Unsafe("The Minecraft artifact contains an entry outside approved size limits.");
    }

    private static bool ShouldExtract(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length == 1
            ? AllowedRootFiles.Contains(segments[0], StringComparer.Ordinal)
            : AllowedDirectories.Contains(segments[0], StringComparer.Ordinal);
    }

    private static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > expectedLength || total > MaximumEntryBytes)
                throw Unsafe("A Minecraft artifact entry exceeds its approved size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }

    private static async Task WriteMarkerAsync(
        string stagingRoot,
        MinecraftInitialProvisioningConfiguration intent,
        ExtractionSummary extraction,
        CancellationToken cancellationToken)
    {
        var marker = new ArtifactMarker(
            intent.PackId,
            intent.PackVersion,
            intent.CurseForgeProjectId,
            intent.CurseForgeFileId,
            intent.ArtifactFileName,
            intent.ArtifactSha256,
            intent.ArtifactSizeBytes,
            extraction.EntryCount,
            extraction.ExpandedBytes);
        await using var stream = new FileStream(
            Path.Combine(stagingRoot, MarkerFileName),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, marker, MarkerJson, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static PublishedState InspectPublished(
        string path,
        MinecraftInitialProvisioningConfiguration intent)
    {
        if (!Directory.Exists(path)) return PublishedState.EmptyOrAbsent;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return PublishedState.Conflict;
        if (!Directory.EnumerateFileSystemEntries(path).Any()) return PublishedState.EmptyOrAbsent;
        var markerPath = Path.Combine(path, MarkerFileName);
        if (!File.Exists(markerPath)) return PublishedState.Conflict;
        var marker = JsonSerializer.Deserialize<ArtifactMarker>(File.ReadAllText(markerPath), MarkerJson);
        if (marker is null
            || marker.PackId != intent.PackId
            || marker.PackVersion != intent.PackVersion
            || marker.CurseForgeProjectId != intent.CurseForgeProjectId
            || marker.CurseForgeFileId != intent.CurseForgeFileId
            || marker.FileName != intent.ArtifactFileName
            || marker.Sha256 != intent.ArtifactSha256
            || marker.ArchiveSizeBytes != intent.ArtifactSizeBytes
            || marker.ExtractedEntryCount <= 0
            || marker.ExtractedBytes <= 0
            || !AllowedDirectories.All(directory => Directory.Exists(Path.Combine(path, directory)))
            || !File.Exists(Path.Combine(path, "server.properties")))
            return PublishedState.Conflict;
        return PublishedState.Valid;
    }

    private static ArtifactPaths ResolvePaths(string targetRoot, string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        if (operationId.Length is 0 or > 64
            || operationId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
            throw new MinecraftArtifactException(
                MinecraftArtifactErrorCodes.TargetConflict,
                "Minecraft artifact operation identity is invalid.");
        var target = Path.GetFullPath(targetRoot);
        var parent = Directory.GetParent(target)?.FullName
            ?? throw new MinecraftArtifactException(
                MinecraftArtifactErrorCodes.TargetConflict,
                "Minecraft artifact target is invalid.");
        var staging = EnsureContained(parent, $"{Path.GetFileName(target)}.staging-{operationId}");
        var archive = EnsureContained(parent, $"{Path.GetFileName(target)}.download-{operationId}.zip");
        return new(target, staging, archive);
    }

    private static string EnsureContained(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw Unsafe("Minecraft artifact path escaped managed storage.");
        return candidate;
    }

    private static void ResetStaging(ArtifactPaths paths)
    {
        if (Directory.Exists(paths.Staging))
        {
            if ((File.GetAttributes(paths.Staging) & FileAttributes.ReparsePoint) != 0)
                throw Unsafe("Minecraft artifact staging is unsafe.");
            Directory.Delete(paths.Staging, recursive: true);
        }
        if (File.Exists(paths.Archive)) File.Delete(paths.Archive);
    }

    private static MinecraftArtifactAcquisitionResult Failed(
        string code,
        string message,
        bool outcomeUnknown = false) => new(false, code, message, outcomeUnknown);

    private static MinecraftArtifactException Unsafe(string message) =>
        new(MinecraftArtifactErrorCodes.ArchiveUnsafe, message);

    private enum PublishedState { EmptyOrAbsent, Valid, Conflict }
    private sealed record ArtifactPaths(string Target, string Staging, string Archive);
    private sealed record ExtractionSummary(int EntryCount, long ExpandedBytes);
    private sealed record ArtifactMarker(
        string PackId,
        string PackVersion,
        int CurseForgeProjectId,
        int CurseForgeFileId,
        string FileName,
        string Sha256,
        long ArchiveSizeBytes,
        int ExtractedEntryCount,
        long ExtractedBytes);
}

public sealed class AcquireMinecraftArtifactProvisioningStep(
    IMinecraftProvisioningIntentReader intentReader,
    IMinecraftArtifactAcquisitionService acquisition) : IProvisioningStep
{
    public string Id => ProvisioningStepIds.AcquireGameArtifact;

    public async Task<ProvisioningStepResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken)
    {
        if (context.ValidatedPlan.GameId.Value != "minecraft")
            return ProvisioningStepResult.Failure(
                MinecraftArtifactErrorCodes.TargetConflict,
                "The game artifact step is not valid for this game.");
        var target = context.ValidatedPlan.Storage.Single(item => item.DefinitionId == "data").ApiPath;
        if (string.IsNullOrWhiteSpace(target))
            return ProvisioningStepResult.Failure(
                MinecraftArtifactErrorCodes.TargetConflict,
                "Managed Minecraft storage is unavailable.");
        var intent = await intentReader.ReadAsync(context.GameServerId, cancellationToken);
        var result = await acquisition.AcquireAsync(target, context.OperationId, intent, cancellationToken);
        return result.Succeeded
            ? ProvisioningStepResult.Success()
            : ProvisioningStepResult.Failure(
                result.SafeCode!, result.SafeMessage!,
                result.OutcomeUnknown ? ProvisioningFailureTypes.Unknown : ProvisioningFailureTypes.Transient);
    }
}

public sealed class AcquireMinecraftArtifactReconciler(
    IProvisioningContextLoader contexts,
    IMinecraftProvisioningIntentReader intentReader,
    IMinecraftArtifactAcquisitionService acquisition) : IProvisioningStepReconciler
{
    public string StepId => ProvisioningStepIds.AcquireGameArtifact;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step,
        CancellationToken cancellationToken)
    {
        var context = await contexts.LoadAsync(operation.OperationId, cancellationToken);
        var target = context.ValidatedPlan.Storage.Single(item => item.DefinitionId == "data").ApiPath;
        if (string.IsNullOrWhiteSpace(target))
            return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed Minecraft storage is unavailable.");
        var intent = await intentReader.ReadAsync(context.GameServerId, cancellationToken);
        return await acquisition.InspectAsync(target, operation.OperationId, intent, cancellationToken);
    }
}

public sealed class MinecraftArtifactException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}
