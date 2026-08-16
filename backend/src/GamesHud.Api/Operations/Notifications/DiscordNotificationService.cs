using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Operations.Notifications;

public sealed class DiscordNotificationService : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<NotificationOptions> _options;
    private readonly NotificationRuntimeState _state;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(
        HttpClient httpClient,
        IOptions<NotificationOptions> options,
        NotificationRuntimeState state,
        TimeProvider timeProvider,
        ILogger<DiscordNotificationService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _state = state;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public NotificationSettingsResponse GetSettings()
    {
        var discord = ResolveDiscordOptions();

        return new NotificationSettingsResponse(
            DiscordWebhookConfigured: IsWebhookConfigured(discord),
            ServerStatusEnabled: discord.Events.ServerStatus,
            BackupsEnabled: discord.Events.Backups,
            UpdatesEnabled: discord.Events.Updates,
            PlayerJoinLeaveEnabled: discord.Events.PlayerJoinLeave,
            CooldownSeconds: ResolveCooldownSeconds(discord),
            LastTestAt: _state.LastTestAt?.ToString("O", CultureInfo.InvariantCulture),
            LastTestResult: _state.LastTestResult);
    }

    public async Task<NotificationSendResult> SendTestAsync(CancellationToken cancellationToken)
    {
        var result = await NotifyAsync(
            new NotificationEvent(
                NotificationEventTypes.Test,
                "GamesHud test notification",
                "Discord webhook notifications are configured."),
            cancellationToken);

        _state.SetLastTest(result.CompletedAt, result.Message);

        return result;
    }

    public async Task<NotificationSendResult> NotifyAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken)
    {
        var discord = ResolveDiscordOptions();
        var now = _timeProvider.GetUtcNow();

        if (!IsWebhookConfigured(discord))
        {
            return new NotificationSendResult(false, "Discord webhook is not configured.", now);
        }

        if (!IsEventEnabled(notificationEvent.Type, discord.Events))
        {
            return new NotificationSendResult(false, "Notification event is disabled.", now);
        }

        var cooldown = TimeSpan.FromSeconds(ResolveCooldownSeconds(discord));
        var deduplicationKey = notificationEvent.DeduplicationKey ?? notificationEvent.Type;

        if (!notificationEvent.Type.Equals(NotificationEventTypes.Test, StringComparison.Ordinal)
            && !_state.CanSend(deduplicationKey, now, cooldown))
        {
            return new NotificationSendResult(false, "Notification skipped by cooldown.", now);
        }

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(CreatePayload(notificationEvent), JsonOptions),
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.PostAsync(discord.WebhookUrl.Trim(), content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new NotificationSendResult(
                    false,
                    $"Discord webhook returned HTTP {(int)response.StatusCode}.",
                    _timeProvider.GetUtcNow());
            }

            var completedAt = _timeProvider.GetUtcNow();
            _state.MarkSent(deduplicationKey, completedAt);

            return new NotificationSendResult(true, "Notification sent.", completedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Discord notification failed.");

            return new NotificationSendResult(false, "Discord notification failed.", _timeProvider.GetUtcNow());
        }
    }

    private DiscordNotificationOptions ResolveDiscordOptions()
    {
        return _options.Value.Discord;
    }

    private static bool IsWebhookConfigured(DiscordNotificationOptions options)
    {
        return Uri.TryCreate(options.WebhookUrl?.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveCooldownSeconds(DiscordNotificationOptions options)
    {
        return Math.Clamp(options.CooldownSeconds, 5, 3600);
    }

    private static bool IsEventEnabled(
        string eventType,
        DiscordNotificationEventOptions options)
    {
        return eventType switch
        {
            NotificationEventTypes.ServerStarted
                or NotificationEventTypes.ServerStopped
                or NotificationEventTypes.RestartCompleted
                or NotificationEventTypes.ServerUnhealthy => options.ServerStatus,
            NotificationEventTypes.BackupCompleted
                or NotificationEventTypes.BackupFailed => options.Backups,
            NotificationEventTypes.UpdateAvailable
                or NotificationEventTypes.UpdateCompleted => options.Updates,
            NotificationEventTypes.PlayerJoined
                or NotificationEventTypes.PlayerLeft => options.PlayerJoinLeave,
            NotificationEventTypes.Test => true,
            _ => false
        };
    }

    private static DiscordWebhookPayload CreatePayload(NotificationEvent notificationEvent)
    {
        return new DiscordWebhookPayload(
            Username: "GamesHud",
            Content: null,
            Embeds:
            [
                new DiscordWebhookEmbed(
                    Title: notificationEvent.Title,
                    Description: notificationEvent.Message,
                    Color: ResolveColor(notificationEvent.Type))
            ]);
    }

    private static int ResolveColor(string eventType)
    {
        return eventType switch
        {
            NotificationEventTypes.BackupFailed
                or NotificationEventTypes.ServerUnhealthy => 15_557_587,
            NotificationEventTypes.UpdateAvailable => 16_180_291,
            NotificationEventTypes.BackupCompleted
                or NotificationEventTypes.UpdateCompleted
                or NotificationEventTypes.RestartCompleted
                or NotificationEventTypes.ServerStarted => 5_765_004,
            _ => 3_719_400
        };
    }

    private sealed record DiscordWebhookPayload(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("embeds")] IReadOnlyCollection<DiscordWebhookEmbed> Embeds);

    private sealed record DiscordWebhookEmbed(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("color")] int Color);
}
