namespace Glance.Server;

internal sealed class BackupSchedulerService : BackgroundService
{
    private readonly MaintenanceService _maintenance;
    private readonly DataSafetyStartupState _startupState;
    private readonly ILogger<BackupSchedulerService> _logger;

    public BackupSchedulerService(
        MaintenanceService maintenance,
        DataSafetyStartupState startupState,
        ILogger<BackupSchedulerService> logger)
    {
        _maintenance = maintenance;
        _startupState = startupState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_startupState.Info.Healthy) continue;
            try
            {
                await _maintenance.RunAutomaticBackupIfDueAsync(TimeProvider.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup check failed");
            }
        }
    }
}
