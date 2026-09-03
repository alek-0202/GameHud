namespace GamesHud.Api.Configuration;

public sealed class RuntimeHealthOptions
{
    public const string SectionName = "RuntimeHealth";
    public const int MaximumTimeoutSeconds = 600;
    public const int MaximumPollIntervalSeconds = 30;
    public int TimeoutSeconds { get; init; } = 60;
    public int PollIntervalSeconds { get; init; } = 2;
}
