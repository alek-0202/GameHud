using System.Globalization;

namespace GamesHud.Api.Metrics.Services;

public sealed record LinuxMemoryInfo(
    ulong TotalBytes,
    ulong AvailableBytes);

public static class LinuxMemoryInfoParser
{
    public static LinuxMemoryInfo Parse(string memInfoText)
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

        return new LinuxMemoryInfo(total * 1024, available * 1024);
    }

    private static ulong ParseKilobytes(string value)
    {
        var numeric = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        return ulong.Parse(numeric, CultureInfo.InvariantCulture);
    }
}
