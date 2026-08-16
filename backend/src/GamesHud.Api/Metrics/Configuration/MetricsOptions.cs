namespace GamesHud.Api.Metrics.Configuration;

public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    public int SnapshotIntervalSeconds { get; set; } = 60;

    public int RetentionHours { get; set; } = 24;

    public string HostDiskPath { get; set; } = "/";
}
