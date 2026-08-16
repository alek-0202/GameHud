namespace GamesHud.Api.Metrics.Services;

public static class MetricsCalculator
{
    public static double? CalculateCpuPercent(
        ulong currentTotal,
        ulong currentIdle,
        ulong previousTotal,
        ulong previousIdle)
    {
        if (currentTotal <= previousTotal || currentIdle < previousIdle)
        {
            return null;
        }

        var totalDelta = currentTotal - previousTotal;
        var idleDelta = currentIdle - previousIdle;

        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return null;
        }

        return Math.Round((totalDelta - idleDelta) / (double)totalDelta * 100, 2);
    }

    public static double? CalculateDockerCpuPercent(
        ulong currentCpuUsage,
        ulong previousCpuUsage,
        ulong currentSystemUsage,
        ulong previousSystemUsage,
        uint onlineCpus)
    {
        if (currentCpuUsage <= previousCpuUsage || currentSystemUsage <= previousSystemUsage)
        {
            return null;
        }

        var cpuDelta = currentCpuUsage - previousCpuUsage;
        var systemDelta = currentSystemUsage - previousSystemUsage;
        var cpuCount = onlineCpus == 0 ? 1 : onlineCpus;

        return Math.Round(cpuDelta / (double)systemDelta * cpuCount * 100, 2);
    }

    public static ulong CalculateContainerMemoryUsage(
        ulong usage,
        IDictionary<string, ulong>? stats)
    {
        if (stats is not null
            && stats.TryGetValue("cache", out var cache)
            && usage > cache)
        {
            return usage - cache;
        }

        return usage;
    }
}
