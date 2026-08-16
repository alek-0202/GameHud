namespace GamesHud.Api.Operations.Notifications;

public static class NotificationEventTypes
{
    public const string ServerStarted = "server-started";
    public const string ServerStopped = "server-stopped";
    public const string RestartCompleted = "restart-completed";
    public const string UpdateAvailable = "update-available";
    public const string UpdateCompleted = "update-completed";
    public const string BackupFailed = "backup-failed";
    public const string BackupCompleted = "backup-completed";
    public const string PlayerJoined = "player-joined";
    public const string PlayerLeft = "player-left";
    public const string ServerUnhealthy = "server-unhealthy";
    public const string Test = "test";
}

public sealed record NotificationEvent(
    string Type,
    string Title,
    string Message,
    string? DeduplicationKey = null);

public sealed record NotificationSendResult(
    bool Success,
    string Message,
    DateTimeOffset CompletedAt);

public sealed class NotificationRuntimeState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSentByKey = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastTestAt;
    private string? _lastTestResult;

    public DateTimeOffset? LastTestAt
    {
        get
        {
            lock (_lock)
            {
                return _lastTestAt;
            }
        }
    }

    public string? LastTestResult
    {
        get
        {
            lock (_lock)
            {
                return _lastTestResult;
            }
        }
    }

    public bool CanSend(
        string key,
        DateTimeOffset now,
        TimeSpan cooldown)
    {
        lock (_lock)
        {
            return !_lastSentByKey.TryGetValue(key, out var lastSent)
                || now - lastSent >= cooldown;
        }
    }

    public void MarkSent(
        string key,
        DateTimeOffset sentAt)
    {
        lock (_lock)
        {
            _lastSentByKey[key] = sentAt;
        }
    }

    public void SetLastTest(
        DateTimeOffset testedAt,
        string result)
    {
        lock (_lock)
        {
            _lastTestAt = testedAt;
            _lastTestResult = result;
        }
    }
}
