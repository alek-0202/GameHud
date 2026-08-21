using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.Metrics.Configuration;
using GamesHud.Api.Metrics.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.HostCapabilities.Services;

public interface IHostResourceInspector
{
    Task<HostResourceInspection> GetResourcesAsync(CancellationToken cancellationToken);
}

public sealed class HostResourceInspector : IHostResourceInspector
{
    private const string ProcMemInfoPath = "/proc/meminfo";
    private readonly IHostSystemInfoProvider _systemInfoProvider;
    private readonly IHostMetricsFileSystem _fileSystem;
    private readonly IHostStorageInfoProvider _storageInfoProvider;
    private readonly IOptions<MetricsOptions> _options;

    public HostResourceInspector(
        IHostSystemInfoProvider systemInfoProvider,
        IHostMetricsFileSystem fileSystem,
        IHostStorageInfoProvider storageInfoProvider,
        IOptions<MetricsOptions> options)
    {
        _systemInfoProvider = systemInfoProvider;
        _fileSystem = fileSystem;
        _storageInfoProvider = storageInfoProvider;
        _options = options;
    }

    public async Task<HostResourceInspection> GetResourcesAsync(CancellationToken cancellationToken)
    {
        var issues = new List<HostCapabilityIssue>();
        var memory = await GetMemoryAsync(issues, cancellationToken);
        var storage = GetStorage(issues);

        return new HostResourceInspection(memory, storage, issues);
    }

    private async Task<HostMemoryInfo> GetMemoryAsync(
        List<HostCapabilityIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!_systemInfoProvider.IsLinux)
        {
            issues.Add(new HostCapabilityIssue(
                "memory_unavailable",
                HostCapabilityIssueSeverities.Info,
                "Memory details are not available for this host platform yet."));

            return new HostMemoryInfo(HostCapabilityStatuses.Unavailable, null, null);
        }

        try
        {
            var memory = LinuxMemoryInfoParser.Parse(
                await _fileSystem.ReadAllTextAsync(ProcMemInfoPath, cancellationToken));

            return new HostMemoryInfo(
                HostCapabilityStatuses.Available,
                memory.TotalBytes,
                memory.AvailableBytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            issues.Add(new HostCapabilityIssue(
                "memory_unavailable",
                HostCapabilityIssueSeverities.Warning,
                "Memory details could not be inspected from this host."));

            return new HostMemoryInfo(HostCapabilityStatuses.Unavailable, null, null);
        }
    }

    private HostStorageInfo GetStorage(List<HostCapabilityIssue> issues)
    {
        var path = string.IsNullOrWhiteSpace(_options.Value.HostDiskPath)
            ? AppContext.BaseDirectory
            : _options.Value.HostDiskPath;

        try
        {
            var drive = _storageInfoProvider.GetDriveInfo(path);

            return new HostStorageInfo(
                HostCapabilityStatuses.Available,
                drive.Name,
                drive.TotalBytes,
                drive.AvailableBytes);
        }
        catch (Exception)
        {
            issues.Add(new HostCapabilityIssue(
                "storage_unavailable",
                HostCapabilityIssueSeverities.Blocking,
                "Primary storage could not be inspected."));

            return new HostStorageInfo(HostCapabilityStatuses.Unavailable, string.Empty, null, null);
        }
    }
}

public interface IHostStorageInfoProvider
{
    HostStorageDriveInfo GetDriveInfo(string path);
}

public sealed record HostStorageDriveInfo(
    string Name,
    ulong TotalBytes,
    ulong AvailableBytes);

public sealed class RuntimeHostStorageInfoProvider : IHostStorageInfoProvider
{
    private readonly IHostMetricsFileSystem _fileSystem;

    public RuntimeHostStorageInfoProvider(IHostMetricsFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public HostStorageDriveInfo GetDriveInfo(string path)
    {
        var drive = _fileSystem.GetDriveInfo(path);

        return new HostStorageDriveInfo(
            drive.Name,
            checked((ulong)drive.TotalSize),
            checked((ulong)drive.AvailableFreeSpace));
    }
}
