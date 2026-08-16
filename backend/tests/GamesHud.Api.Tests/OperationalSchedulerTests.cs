using GamesHud.Api.Operations.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesHud.Api.Tests;

public sealed class OperationalSchedulerTests
{
    [Fact]
    public void DisabledScheduleDoesNotBecomeDue()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-02T03:00:00Z"));
        var store = new InMemoryScheduleStore(timeProvider);
        var task = SchedulerValidation.NormalizeTask(
            new ScheduleTaskRequest(
                "nightly-restart",
                ScheduleActionTypes.RestartPalworld,
                10,
                Enabled: false,
                null),
            timeProvider);

        var saved = store.Upsert(task);

        Assert.False(saved.Enabled);
        Assert.Null(saved.NextRunAt);
        Assert.Equal("disabled", saved.Status);
    }

    [Fact]
    public async Task DueScheduleRunsAndComputesNextRun()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-02T03:00:00Z"));
        var executor = new RecordingScheduledOperationExecutor();
        var service = CreateService(timeProvider, executor, out var store);
        service.UpsertTask(new ScheduleTaskRequest(
            "update-check",
            ScheduleActionTypes.UpdateCheck,
            5,
            Enabled: true,
            null));
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        await service.ExecuteDueTasksAsync(CancellationToken.None);

        var task = store.Get("update-check");
        Assert.NotNull(task);
        Assert.Equal(1, executor.ExecutionCount);
        Assert.Equal("executed-update-check", task.LastResult);
        Assert.Equal(timeProvider.GetUtcNow().AddMinutes(5), task.NextRunAt);
    }

    [Fact]
    public async Task DuplicateExecutionIsRejected()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-02T03:00:00Z"));
        var service = CreateService(timeProvider, new RecordingScheduledOperationExecutor(), out var store);
        service.UpsertTask(new ScheduleTaskRequest(
            "auto-backup",
            ScheduleActionTypes.AutomaticBackup,
            5,
            Enabled: true,
            null));
        Assert.True(store.TryMarkRunning("auto-backup"));

        var exception = await Assert.ThrowsAsync<SchedulerValidationException>(
            () => service.RunTaskAsync("auto-backup", CancellationToken.None));

        Assert.Contains("already running", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationIsPropagatedAndTaskIsReleased()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-02T03:00:00Z"));
        var service = CreateService(
            timeProvider,
            new CancellingScheduledOperationExecutor(),
            out var store);
        service.UpsertTask(new ScheduleTaskRequest(
            "announcement",
            ScheduleActionTypes.Announcement,
            5,
            Enabled: true,
            "Restart soon."));
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RunTaskAsync("announcement", cancellationTokenSource.Token));

        Assert.True(store.TryMarkRunning("announcement"));
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("docker-exec")]
    [InlineData("compose-down")]
    public void UnsupportedActionsAreRejected(string actionType)
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        Assert.Throws<SchedulerValidationException>(
            () => SchedulerValidation.NormalizeTask(
                new ScheduleTaskRequest("unsafe-task", actionType, 5, true, null),
                timeProvider));
    }

    private static SchedulerService CreateService(
        TimeProvider timeProvider,
        IScheduledOperationExecutor executor,
        out InMemoryScheduleStore store)
    {
        store = new InMemoryScheduleStore(timeProvider);
        var services = new ServiceCollection();
        services.AddScoped(_ => executor);
        var provider = services.BuildServiceProvider();

        return new SchedulerService(
            store,
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider,
            NullLogger<SchedulerService>.Instance);
    }

    private sealed class RecordingScheduledOperationExecutor : IScheduledOperationExecutor
    {
        public int ExecutionCount { get; private set; }

        public Task<ScheduledOperationResult> ExecuteAsync(
            ScheduleTask task,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;

            return Task.FromResult(new ScheduledOperationResult(true, $"executed-{task.ActionType}"));
        }
    }

    private sealed class CancellingScheduledOperationExecutor : IScheduledOperationExecutor
    {
        public Task<ScheduledOperationResult> ExecuteAsync(
            ScheduleTask task,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new ScheduledOperationResult(true, "unexpected"));
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan timeSpan)
        {
            _utcNow = _utcNow.Add(timeSpan);
        }
    }
}
