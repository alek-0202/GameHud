namespace GamesHud.Api.Operations.Scheduling;

public interface ISchedulerService
{
    IReadOnlyCollection<ScheduleTaskResponse> GetTasks();

    ScheduleTaskResponse UpsertTask(ScheduleTaskRequest request);

    Task<ScheduleRunResponse> RunTaskAsync(
        string id,
        CancellationToken cancellationToken);

    Task ExecuteDueTasksAsync(CancellationToken cancellationToken);
}
