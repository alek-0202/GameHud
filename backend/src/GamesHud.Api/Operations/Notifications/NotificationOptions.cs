namespace GamesHud.Api.Operations.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public DiscordNotificationOptions Discord { get; set; } = new();
}

public sealed class DiscordNotificationOptions
{
    public string WebhookUrl { get; set; } = string.Empty;

    public int CooldownSeconds { get; set; } = 60;

    public DiscordNotificationEventOptions Events { get; set; } = new();
}

public sealed class DiscordNotificationEventOptions
{
    public bool ServerStatus { get; set; } = true;

    public bool Backups { get; set; } = true;

    public bool Updates { get; set; } = true;

    public bool PlayerJoinLeave { get; set; }
}
