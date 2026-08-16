namespace GamesHud.Api.Palworld.Backups.Services;

public sealed class PalworldBackupScheduleState
{
    private readonly object _lock = new();
    private DateTimeOffset? _nextScheduledAt;

    public DateTimeOffset? NextScheduledAt
    {
        get
        {
            lock (_lock)
            {
                return _nextScheduledAt;
            }
        }
    }

    public void SetNextScheduledAt(DateTimeOffset? nextScheduledAt)
    {
        lock (_lock)
        {
            _nextScheduledAt = nextScheduledAt;
        }
    }
}
