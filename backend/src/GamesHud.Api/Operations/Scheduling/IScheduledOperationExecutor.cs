namespace GamesHud.Api.Operations.Scheduling;

public interface IScheduledOperationExecutor
{
    Task<ScheduledOperationResult> ExecuteAsync(
        ScheduleTask task,
        CancellationToken cancellationToken);
}
