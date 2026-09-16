namespace GamesHud.Api.Configuration;

public sealed class RuntimeImageAcquisitionOptions
{
    public const string SectionName = "RuntimeImageAcquisition";
    public const int MaximumTimeoutSeconds = 3600;
    public const int MaximumInspectTimeoutSeconds = 120;
    public int TimeoutSeconds { get; init; } = 900;
    public int InspectTimeoutSeconds { get; init; } = 30;

    public bool IsValid => TimeoutSeconds is > 0 and <= MaximumTimeoutSeconds
        && InspectTimeoutSeconds is > 0 and <= MaximumInspectTimeoutSeconds;
}
