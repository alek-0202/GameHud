namespace GamesHud.Api.Configuration;

public sealed class RuntimeHealthOptions
{
    public const string SectionName = "RuntimeHealth";
    public const int MaximumTimeoutSeconds = 600;
    public const int MaximumGameTimeoutSeconds = 1800;
    public const int MaximumPollIntervalSeconds = 30;
    public int TimeoutSeconds { get; init; } = 60;
    public int PalworldTimeoutSeconds { get; init; } = 600;
    public int PollIntervalSeconds { get; init; } = 2;
}
