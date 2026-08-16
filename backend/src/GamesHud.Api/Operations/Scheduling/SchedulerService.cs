using System.Globalization;

namespace GamesHud.Api.Operations.Scheduling;

public sealed class SchedulerService : ISchedulerService
{
    private readonly IScheduleStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(
        IScheduleStore store,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SchedulerService> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyCollection<ScheduleTaskResponse> GetTasks()
    {
        return _store.GetAll()
            .Select(task => task.ToResponse())
            .ToArray();
    }

    public ScheduleTaskResponse UpsertTask(ScheduleTaskRequest request)
    {
        var task = SchedulerValidation.NormalizeTask(request, _timeProvider);

        return _store.Upsert(task).ToResponse();
    }

    public async Task<ScheduleRunResponse> RunTaskAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var task = _store.Get(id)
            ?? throw new SchedulerValidationException("Schedule task was not found.");

        if (!_store.TryMarkRunning(id))
        {
            throw new SchedulerValidationException("Schedule task is disabled, missing or already running.");
        }

        var completedAt = _timeProvider.GetUtcNow();
        ScheduledOperationResult result;

        try
        {
            result = await ExecuteTaskAsync(task, cancellationToken);
            completedAt = _timeProvider.GetUtcNow();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Scheduled operation {TaskId} failed.", task.Id);

            result = new ScheduledOperationResult(false, "failed");
            completedAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _store.MarkCompleted(
                    id,
                    new ScheduledOperationResult(false, "cancelled"),
                    _timeProvider.GetUtcNow());
            }
        }

        _store.MarkCompleted(id, result, completedAt);

        return new ScheduleRunResponse(
            id,
            result.Success,
            result.Result,
            completedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public async Task ExecuteDueTasksAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var dueTasks = _store.GetAll()
            .Where(task => task.Enabled && task.NextRunAt <= now)
            .ToArray();

        foreach (var task in dueTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryMarkRunning(task.Id))
            {
                continue;
            }

            var completedAt = _timeProvider.GetUtcNow();
            ScheduledOperationResult result;

            try
            {
                result = await ExecuteTaskAsync(task, cancellationToken);
                completedAt = _timeProvider.GetUtcNow();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Scheduled operation {TaskId} failed.", task.Id);

                result = new ScheduledOperationResult(false, "failed");
                completedAt = _timeProvider.GetUtcNow();
            }

            _store.MarkCompleted(task.Id, result, completedAt);
        }
    }

    private async Task<ScheduledOperationResult> ExecuteTaskAsync(
        ScheduleTask task,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IScheduledOperationExecutor>();

        return await executor.ExecuteAsync(task, cancellationToken);
    }
}
