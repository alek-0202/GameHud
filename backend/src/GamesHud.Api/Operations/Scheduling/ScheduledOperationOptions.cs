namespace GamesHud.Api.Operations.Scheduling;

public sealed class ScheduledOperationOptions
{
    public const string SectionName = "Operations";

    public PalworldScheduledOperationOptions Palworld { get; set; } = new();
}

public sealed class PalworldScheduledOperationOptions
{
    public int LifecycleTimeoutSeconds { get; set; } = 30;

    public int RestartWaitSeconds { get; set; } = 60;

    public IReadOnlyCollection<int> RestartWarningMinutes { get; set; } = [10, 5, 1];
}
