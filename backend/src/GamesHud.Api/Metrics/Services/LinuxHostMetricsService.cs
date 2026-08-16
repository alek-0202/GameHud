using System.Globalization;
using GamesHud.Api.Metrics.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Metrics.Services;

public sealed class LinuxHostMetricsService : IHostMetricsService
{
    private const string ProcStatPath = "/proc/stat";
    private const string ProcMemInfoPath = "/proc/meminfo";
    private const string ProcUptimePath = "/proc/uptime";
    private readonly object _lock = new();

    private readonly IOptions<MetricsOptions> _options;
    private readonly IHostMetricsFileSystem _fileSystem;
    private CpuSample? _previousCpuSample;

    public LinuxHostMetricsService(
        IOptions<MetricsOptions> options,
        IHostMetricsFileSystem fileSystem)
    {
        _options = options;
        _fileSystem = fileSystem;
    }

    public async Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cpuSample = ParseCpuSample(await _fileSystem.ReadAllTextAsync(ProcStatPath, cancellationToken));
            var cpuPercent = CalculateCpuPercent(cpuSample);
            var memory = ParseMemory(await _fileSystem.ReadAllTextAsync(ProcMemInfoPath, cancellationToken));
            var uptimeSeconds = ParseUptime(await _fileSystem.ReadAllTextAsync(ProcUptimePath, cancellationToken));
            var disk = GetDiskUsage();

            return new HostMetrics(
                cpuPercent,
                memory.UsedBytes,
                memory.TotalBytes,
                disk.UsedBytes,
                disk.TotalBytes,
                uptimeSeconds,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new HostMetricsUnavailableException("Host metrics are unavailable.", exception);
        }
    }

    private double? CalculateCpuPercent(CpuSample currentSample)
    {
        lock (_lock)
        {
            var previousSample = _previousCpuSample;
            _previousCpuSample = currentSample;

            return previousSample is null
                ? null
                : MetricsCalculator.CalculateCpuPercent(
                    currentSample.Total,
                    currentSample.Idle,
                    previousSample.Total,
                    previousSample.Idle);
        }
    }

    private DiskUsage GetDiskUsage()
    {
        var path = string.IsNullOrWhiteSpace(_options.Value.HostDiskPath)
            ? "/"
            : _options.Value.HostDiskPath;
        var drive = _fileSystem.GetDriveInfo(path);
        var totalBytes = checked((ulong)drive.TotalSize);
        var freeBytes = checked((ulong)drive.AvailableFreeSpace);

        return new DiskUsage(totalBytes - freeBytes, totalBytes);
    }

    private static CpuSample ParseCpuSample(string statText)
    {
        var firstLine = statText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));

        if (firstLine is null)
        {
            throw new FormatException("/proc/stat did not contain aggregate CPU data.");
        }

        var parts = firstLine
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(part => ulong.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        if (parts.Length < 5)
        {
            throw new FormatException("/proc/stat CPU data is incomplete.");
        }

        var idle = parts[3] + parts[4];
        var total = parts.Aggregate(0UL, (current, value) => current + value);

        return new CpuSample(total, idle);
    }

    private static MemoryUsage ParseMemory(string memInfoText)
    {
        var values = memInfoText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => ParseKilobytes(parts[1]),
                StringComparer.Ordinal);

        var total = values.GetValueOrDefault("MemTotal");
        var available = values.GetValueOrDefault("MemAvailable");

        if (total == 0)
        {
            throw new FormatException("/proc/meminfo did not contain MemTotal.");
        }

        if (available == 0)
        {
            available = values.GetValueOrDefault("MemFree");
        }

        return new MemoryUsage((total - available) * 1024, total * 1024);
    }

    private static ulong ParseKilobytes(string value)
    {
        var numeric = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        return ulong.Parse(numeric, CultureInfo.InvariantCulture);
    }

    private static double ParseUptime(string uptimeText)
    {
        var firstValue = uptimeText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        return double.Parse(firstValue, CultureInfo.InvariantCulture);
    }

    private sealed record CpuSample(ulong Total, ulong Idle);

    private sealed record MemoryUsage(ulong UsedBytes, ulong TotalBytes);

    private sealed record DiskUsage(ulong UsedBytes, ulong TotalBytes);
}
