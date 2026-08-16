using System.Net;
using System.Text.Json;
using GamesHud.Api.Operations.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class DiscordNotificationTests
{
    [Fact]
    public void SettingsNeverExposeWebhookUrl()
    {
        var service = CreateService(
            new RecordingHttpHandler(HttpStatusCode.NoContent),
            webhookUrl: "https://discord.com/api/webhooks/private-token");

        var settings = service.GetSettings();
        var json = JsonSerializer.Serialize(settings);

        Assert.True(settings.DiscordWebhookConfigured);
        Assert.DoesNotContain("private-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("discord.com/api/webhooks", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotificationFailuresReturnFailedResult()
    {
        var service = CreateService(
            new RecordingHttpHandler(HttpStatusCode.InternalServerError),
            webhookUrl: "https://discord.com/api/webhooks/test");

        var result = await service.SendTestAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("HTTP 500", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CooldownPreventsDuplicateNotifications()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-02T03:00:00Z"));
        var handler = new RecordingHttpHandler(HttpStatusCode.NoContent);
        var service = CreateService(
            handler,
            webhookUrl: "https://discord.com/api/webhooks/test",
            cooldownSeconds: 60,
            timeProvider: timeProvider);
        var notification = new NotificationEvent(
            NotificationEventTypes.BackupFailed,
            "Backup failed",
            "Backup failed.",
            "same-backup-failure");

        var first = await service.NotifyAsync(notification, CancellationToken.None);
        var second = await service.NotifyAsync(notification, CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("Notification skipped by cooldown.", second.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        var service = CreateService(
            new CancellingHttpHandler(),
            webhookUrl: "https://discord.com/api/webhooks/test");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SendTestAsync(cancellationTokenSource.Token));
    }

    private static DiscordNotificationService CreateService(
        HttpMessageHandler handler,
        string webhookUrl,
        int cooldownSeconds = 60,
        TimeProvider? timeProvider = null)
    {
        return new DiscordNotificationService(
            new HttpClient(handler),
            Options.Create(new NotificationOptions
            {
                Discord = new DiscordNotificationOptions
                {
                    WebhookUrl = webhookUrl,
                    CooldownSeconds = cooldownSeconds,
                    Events = new DiscordNotificationEventOptions
                    {
                        ServerStatus = true,
                        Backups = true,
                        Updates = true,
                        PlayerJoinLeave = false
                    }
                }
            }),
            new NotificationRuntimeState(),
            timeProvider ?? TimeProvider.System,
            NullLogger<DiscordNotificationService>.Instance);
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public RecordingHttpHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class CancellingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
