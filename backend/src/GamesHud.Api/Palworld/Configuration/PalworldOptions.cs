namespace GamesHud.Api.Palworld.Configuration;

public sealed class PalworldOptions
{
    public const string SectionName = "Palworld";

    public string ManagedPath { get; set; } = string.Empty;

    public string BackupPath { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string ConnectionAddress { get; set; } = string.Empty;

    public PalworldRestApiOptions RestApi { get; set; } = new();

    public PalworldBackupOptions Backups { get; set; } = new();
}

public sealed class PalworldRestApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class PalworldBackupOptions
{
    public bool AutomaticEnabled { get; set; }

    public int AutomaticIntervalMinutes { get; set; } = 360;

    public int RetentionCount { get; set; } = 24;

    public int RetentionDays { get; set; } = 7;

    public int PreBackupSaveDelaySeconds { get; set; } = 2;

    public int LifecycleTimeoutSeconds { get; set; } = 30;
}
