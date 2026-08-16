namespace GamesHud.Api.Operations.Notifications;

public interface INotificationService
{
    NotificationSettingsResponse GetSettings();

    Task<NotificationSendResult> SendTestAsync(CancellationToken cancellationToken);

    Task<NotificationSendResult> NotifyAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken);
}
