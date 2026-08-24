using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.Secrets.Services;

public sealed class FileSecretStore : ISecretStore
{
    public const int FileFormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ISecretStoreLayoutResolver _layoutResolver;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public FileSecretStore(
        ISecretStoreLayoutResolver layoutResolver,
        ISecretProtector protector,
        TimeProvider timeProvider)
    {
        _layoutResolver = layoutResolver;
        _protector = protector;
        _timeProvider = timeProvider;
    }

    public async Task<SecretReference> StoreAsync(
        SecretPurpose purpose,
        SecretValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureAvailable();

        var id = SecretId.New();
        var reference = new SecretReference(id);
        var secretLock = GetLock(id);

        await secretLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var file = CreateSecretFile(id, purpose, value, now, now);

            await WriteFileAsync(reference, file, cancellationToken);
        }
        finally
        {
            secretLock.Release();
        }

        return reference;
    }

    public async Task<SecretValue> GetAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureAvailable();

        var secretLock = GetLock(reference.Id);

        await secretLock.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadFileAsync(reference, cancellationToken);

            return _protector.Unprotect(new ProtectedSecretPayload(
                file.Version,
                file.Algorithm,
                file.Nonce,
                file.Ciphertext,
                file.Tag));
        }
        finally
        {
            secretLock.Release();
        }
    }

    public async Task ReplaceAsync(
        SecretReference reference,
        SecretValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(value);
        EnsureAvailable();

        var secretLock = GetLock(reference.Id);

        await secretLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadFileAsync(reference, cancellationToken);
            var now = _timeProvider.GetUtcNow();
            var replacement = CreateSecretFile(
                reference.Id,
                SecretPurpose.From(existing.Purpose),
                value,
                existing.CreatedAtUtc,
                now);

            await WriteFileAsync(reference, replacement, cancellationToken);
        }
        finally
        {
            secretLock.Release();
        }
    }

    public async Task DeleteAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureAvailable();

        var secretLock = GetLock(reference.Id);

        await secretLock.WaitAsync(cancellationToken);
        try
        {
            var path = GetSecretPath(reference);
            if (!File.Exists(path))
            {
                throw new SecretNotFoundException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
        finally
        {
            secretLock.Release();
        }
    }

    private SecretFile CreateSecretFile(
        SecretId id,
        SecretPurpose purpose,
        SecretValue value,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var protectedPayload = _protector.Protect(value);

        return new SecretFile
        {
            Version = FileFormatVersion,
            Id = id.Value,
            Purpose = purpose.Value,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
            UpdatedAtUtc = updatedAtUtc.ToUniversalTime(),
            Algorithm = protectedPayload.Algorithm,
            Nonce = protectedPayload.Nonce,
            Ciphertext = protectedPayload.Ciphertext,
            Tag = protectedPayload.Tag,
        };
    }

    private async Task<SecretFile> ReadFileAsync(
        SecretReference reference,
        CancellationToken cancellationToken)
    {
        var path = GetSecretPath(reference);

        if (!File.Exists(path))
        {
            throw new SecretNotFoundException();
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = await JsonSerializer.DeserializeAsync<SecretFile>(stream, SerializerOptions, cancellationToken)
                ?? throw new SecretStoreCorruptedException();

            if (file.Id != reference.Id.Value
                || file.Version != FileFormatVersion
                || string.IsNullOrWhiteSpace(file.Purpose)
                || string.IsNullOrWhiteSpace(file.Algorithm)
                || string.IsNullOrWhiteSpace(file.Nonce)
                || string.IsNullOrWhiteSpace(file.Ciphertext)
                || string.IsNullOrWhiteSpace(file.Tag))
            {
                throw new SecretStoreCorruptedException();
            }

            return file;
        }
        catch (JsonException exception)
        {
            throw new SecretStoreCorruptedException(exception);
        }
        catch (IOException exception)
        {
            throw new SecretStoreUnavailableException("secret_store_io_unavailable", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SecretStoreUnavailableException("secret_store_io_unavailable", exception);
        }
    }

    private async Task WriteFileAsync(
        SecretReference reference,
        SecretFile file,
        CancellationToken cancellationToken)
    {
        var layout = _layoutResolver.ResolveLayout();
        Directory.CreateDirectory(layout.SecretsRoot);
        TryApplyDirectoryPermissions(layout.SecretsRoot);

        var path = GetSecretPath(reference);
        var tempPath = GetTempPath(layout.SecretsRoot, reference.Id);
        var payload = JsonSerializer.Serialize(file, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                }))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            TryApplyFilePermissions(tempPath);
            File.Move(tempPath, path, overwrite: true);
            TryApplyFilePermissions(path);
        }
        catch (IOException exception)
        {
            TryDeleteTempFile(tempPath);
            throw new SecretStoreUnavailableException("secret_store_io_unavailable", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            TryDeleteTempFile(tempPath);
            throw new SecretStoreUnavailableException("secret_store_io_unavailable", exception);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private string GetSecretPath(SecretReference reference)
    {
        var layout = _layoutResolver.ResolveLayout();
        var candidate = Path.Combine(layout.SecretsRoot, $"{reference.Id.Value}.json");

        return EnsureContained(layout.SecretsRoot, candidate);
    }

    private static string GetTempPath(string secretsRoot, SecretId id)
    {
        return EnsureContained(
            secretsRoot,
            Path.Combine(secretsRoot, $"{id.Value}.{Guid.NewGuid():N}.tmp"));
    }

    private static string EnsureContained(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecretStoreUnavailableException("secret_store_path_invalid");
        }

        return fullCandidate;
    }

    private SemaphoreSlim GetLock(SecretId id)
    {
        return _locks.GetOrAdd(id.Value, _ => new SemaphoreSlim(1, 1));
    }

    private void EnsureAvailable()
    {
        if (!_protector.IsAvailable(out var errorCode))
        {
            throw new SecretStoreUnavailableException(errorCode ?? "secret_store_unavailable");
        }
    }

    private static void TryApplyDirectoryPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
        }
    }

    private static void TryApplyFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class SecretFile
    {
        public int Version { get; init; }

        public string Id { get; init; } = string.Empty;

        public string Purpose { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }

        public string Algorithm { get; init; } = string.Empty;

        public string Nonce { get; init; } = string.Empty;

        public string Ciphertext { get; init; } = string.Empty;

        public string Tag { get; init; } = string.Empty;
    }
}
