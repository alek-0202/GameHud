namespace GamesHud.Api.Operations.Scheduling;

public interface IScheduleStore
{
    IReadOnlyCollection<ScheduleTask> GetAll();

    ScheduleTask? Get(string id);

    ScheduleTask Upsert(ScheduleTask task);

    bool TryMarkRunning(string id);

    void MarkCompleted(
        string id,
        ScheduledOperationResult result,
        DateTimeOffset completedAt);
}
