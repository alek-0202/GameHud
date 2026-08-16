using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Backups.Services;

public sealed class PalworldBackupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PalworldOptions> _options;
    private readonly PalworldBackupScheduleState _scheduleState;
    private readonly ILogger<PalworldBackupScheduler> _logger;

    public PalworldBackupScheduler(
        IServiceScopeFactory scopeFactory,
        IOptions<PalworldOptions> options,
        PalworldBackupScheduleState scheduleState,
        ILogger<PalworldBackupScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _scheduleState = scheduleState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backupOptions = ResolveBackupOptions();

        if (!backupOptions.AutomaticEnabled)
        {
            _scheduleState.SetNextScheduledAt(null);
            return;
        }

        var interval = TimeSpan.FromMinutes(backupOptions.AutomaticIntervalMinutes);
        _scheduleState.SetNextScheduledAt(DateTimeOffset.UtcNow.Add(interval));

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _scheduleState.SetNextScheduledAt(DateTimeOffset.UtcNow.Add(interval));

                using var scope = _scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IPalworldBackupService>();

                await backupService.CreateBackupAsync(
                    new PalworldBackupCreateOptions(
                        PalworldBackupTypes.Automatic,
                        "Automatic scheduled backup.",
                        RequestWorldSave: true),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Automatic Palworld backup failed.");
            }
        }
    }

    private PalworldBackupOptions ResolveBackupOptions()
    {
        var options = _options.Value.Backups;

        return new PalworldBackupOptions
        {
            AutomaticEnabled = options.AutomaticEnabled,
            AutomaticIntervalMinutes = Math.Clamp(options.AutomaticIntervalMinutes, 5, 10_080),
            RetentionCount = Math.Clamp(options.RetentionCount, 1, 10_000),
            RetentionDays = Math.Clamp(options.RetentionDays, 1, 365),
            PreBackupSaveDelaySeconds = Math.Clamp(options.PreBackupSaveDelaySeconds, 0, 30),
            LifecycleTimeoutSeconds = Math.Clamp(options.LifecycleTimeoutSeconds, 1, 120)
        };
    }
}
