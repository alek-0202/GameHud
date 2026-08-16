namespace GamesHud.Api.Operations.Notifications;

public sealed record NotificationSettingsResponse(
    bool DiscordWebhookConfigured,
    bool ServerStatusEnabled,
    bool BackupsEnabled,
    bool UpdatesEnabled,
    bool PlayerJoinLeaveEnabled,
    int CooldownSeconds,
    string? LastTestAt,
    string? LastTestResult);

public sealed record NotificationTestResponse(
    bool Success,
    string Message,
    string CompletedAt);
