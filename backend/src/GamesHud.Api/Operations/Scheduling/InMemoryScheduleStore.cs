namespace GamesHud.Api.Operations.Scheduling;

public sealed class InMemoryScheduleStore : IScheduleStore
{
    private readonly object _lock = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ScheduleTask> _tasks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _runningTaskIds = new(StringComparer.Ordinal);

    public InMemoryScheduleStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        SeedDefaults();
    }

    public IReadOnlyCollection<ScheduleTask> GetAll()
    {
        lock (_lock)
        {
            return _tasks.Values
                .OrderBy(task => task.ActionType, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public ScheduleTask? Get(string id)
    {
        lock (_lock)
        {
            return _tasks.GetValueOrDefault(id);
        }
    }

    public ScheduleTask Upsert(ScheduleTask task)
    {
        lock (_lock)
        {
            var normalizedTask = task.Enabled
                ? task with
                {
                    NextRunAt = task.NextRunAt ?? _timeProvider.GetUtcNow().AddMinutes(task.RecurrenceMinutes),
                    Status = task.Status.Equals("running", StringComparison.Ordinal) ? task.Status : "scheduled"
                }
                : task with
                {
                    NextRunAt = null,
                    Status = "disabled"
                };

            _tasks[normalizedTask.Id] = normalizedTask;

            return normalizedTask;
        }
    }

    public bool TryMarkRunning(string id)
    {
        lock (_lock)
        {
            if (!_tasks.TryGetValue(id, out var task)
                || !task.Enabled
                || _runningTaskIds.Contains(id))
            {
                return false;
            }

            _runningTaskIds.Add(id);
            _tasks[id] = task with { Status = "running" };

            return true;
        }
    }

    public void MarkCompleted(
        string id,
        ScheduledOperationResult result,
        DateTimeOffset completedAt)
    {
        lock (_lock)
        {
            _runningTaskIds.Remove(id);

            if (!_tasks.TryGetValue(id, out var task))
            {
                return;
            }

            _tasks[id] = task with
            {
                LastRunAt = completedAt,
                LastResult = result.Result,
                NextRunAt = task.Enabled ? completedAt.AddMinutes(task.RecurrenceMinutes) : null,
                Status = task.Enabled ? "scheduled" : "disabled"
            };
        }
    }

    private void SeedDefaults()
    {
        var now = _timeProvider.GetUtcNow();
        AddDefault("auto-backup", ScheduleActionTypes.AutomaticBackup, 360, enabled: false, now);
        AddDefault("palworld-restart", ScheduleActionTypes.RestartPalworld, 1440, enabled: false, now);
        AddDefault("update-check", ScheduleActionTypes.UpdateCheck, 360, enabled: false, now);
        AddDefault("announcement", ScheduleActionTypes.Announcement, 60, enabled: false, now);
        AddDefault("palworld-shutdown", ScheduleActionTypes.ShutdownPalworld, 1440, enabled: false, now);
    }

    private void AddDefault(
        string id,
        string actionType,
        int recurrenceMinutes,
        bool enabled,
        DateTimeOffset now)
    {
        _tasks[id] = new ScheduleTask(
            id,
            actionType,
            recurrenceMinutes,
            enabled,
            enabled ? now.AddMinutes(recurrenceMinutes) : null,
            null,
            "never-run",
            enabled ? "scheduled" : "disabled",
            null);
    }
}
