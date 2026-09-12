namespace Glance.Server;

internal sealed class MaintenanceService
{
    private readonly AppPaths _paths;
    private readonly ILogger<MaintenanceService> _logger;
    private readonly TaskRepository _tasks;
    private readonly MaintenanceStateStore _stateStore;
    private readonly AttachmentMaintenance _attachmentMaintenance;
    private readonly DatabaseHealthService _health;
    private readonly DataSafetyService _dataSafety;
    private readonly DataSafetyStartupState _startupState;

    private const int TaskWarningThreshold = 5000;
    private const int AttachmentWarningThreshold = 1000;
    private const long DbSizeWarningThresholdBytes = 200L * 1024 * 1024;

    public MaintenanceService(
        AppPaths paths,
        ILogger<MaintenanceService> logger,
        TaskRepository tasks,
        MaintenanceStateStore stateStore,
        AttachmentMaintenance attachmentMaintenance,
        DatabaseHealthService health,
        DataSafetyService dataSafety,
        DataSafetyStartupState startupState)
    {
        _paths = paths;
        _logger = logger;
        _tasks = tasks;
        _stateStore = stateStore;
        _attachmentMaintenance = attachmentMaintenance;
        _health = health;
        _dataSafety = dataSafety;
        _startupState = startupState;
    }

    public async Task<bool> RunIntegrityCheckAsync(CancellationToken token)
    {
        if (!File.Exists(_paths.DatabasePath)) return false;
        var result = await _health.CheckLiveDatabaseAsync(token);
        await _stateStore.SetIntegrityErrorAsync(!result.IsHealthy);
        if (!result.IsHealthy)
        {
            // Never VACUUM, move, or replace the live database automatically.
            _logger.LogError("Database integrity check failed: {Errors}", string.Join(" ", result.Errors));
        }
        return result.IsHealthy;
    }

    public async Task<bool> CreateBackupAsync(DateTime localNow, CancellationToken token)
    {
        var result = await _dataSafety.CreateBackupAsync("manual", localNow, token);
        return result.Success;
    }

    public Task RecordBackupSuccessAsync(DateTime localNow, string? dayKeyOverride = null) =>
        _stateStore.RecordBackupSuccessAsync(localNow, dayKeyOverride);

    public async Task RunStartupMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        if (!_startupState.Info.Healthy) return;
        await EnsureSearchIndexAsync(token);
        await RunAutomaticBackupIfDueAsync(localNow, token);
        _ = Task.Run(() => _attachmentMaintenance.RunAttachmentGcAsync(CancellationToken.None));
    }

    public async Task RunDailyMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        if (!_startupState.Info.Healthy) return;
        await RunAutomaticBackupIfDueAsync(localNow, token);
        await EnsureSearchIndexAsync(token);
        _ = Task.Run(() => _attachmentMaintenance.RunAttachmentGcAsync(CancellationToken.None));
    }

    internal async Task RunAutomaticBackupIfDueAsync(DateTime localNow, CancellationToken token)
    {
        if (!_startupState.Info.Healthy) return;
        await _dataSafety.ReverifyNewestIfDueAsync(token);
        var settings = await _dataSafety.GetSettingsAsync(token);
        if (!settings.HourlyBackupsEnabled) return;

        var state = await _stateStore.LoadAsync();
        var lastChangeId = await _dataSafety.GetLastChangeIdAsync(token);
        var dayKey = localNow.ToString("yyyy-MM-dd");
        var changed = state.LastBackupAtUtc is null || lastChangeId != state.LastBackupChangeId;
        if (!changed) return;

        var dayChanged = !string.Equals(state.LastBackupDay, dayKey, StringComparison.Ordinal);
        var hourElapsed = state.LastBackupAtUtc is null
            || DateTimeOffset.UtcNow - state.LastBackupAtUtc.Value >= TimeSpan.FromHours(1);
        if (!dayChanged && !hourElapsed) return;

        var reason = dayChanged ? "automatic-daily" : "automatic-hourly";
        var result = await _dataSafety.CreateBackupAsync(reason, localNow, token);
        if (result.Success)
        {
            await _stateStore.RecordBackupSuccessAsync(localNow, dayKey, lastChangeId);
        }
    }

    public async Task<IReadOnlyList<WarningItem>> GetWarningsAsync(CancellationToken token)
    {
        var warnings = new List<WarningItem>();
        var state = await _stateStore.LoadAsync();
        var startup = _startupState.Info;

        if (state.IntegrityError || startup.RecoveryMode)
        {
            warnings.Add(new WarningItem(
                "integrity",
                startup.Message ?? "Database integrity check failed. Glance is in recovery mode and the live database was not replaced."));
        }
        if (!string.IsNullOrWhiteSpace(state.LastBackupError))
        {
            warnings.Add(new WarningItem("backup", $"The last local backup failed: {state.LastBackupError}"));
        }
        if (!string.IsNullOrWhiteSpace(state.MirrorBackupError))
        {
            warnings.Add(new WarningItem("backup-mirror", $"The local backup succeeded, but the additional backup location failed: {state.MirrorBackupError}"));
        }
        if (!string.IsNullOrWhiteSpace(state.BackupVerificationError))
        {
            warnings.Add(new WarningItem("backup-verification", $"A retained backup failed its scheduled verification: {state.BackupVerificationError}"));
        }

        if (!startup.Healthy)
        {
            return warnings;
        }

        var taskCount = await _tasks.GetTaskCountAsync(token);
        if (taskCount >= TaskWarningThreshold)
        {
            warnings.Add(new WarningItem("tasks", $"Large number of tasks detected ({taskCount}). Performance may degrade."));
        }

        if (File.Exists(_paths.DatabasePath))
        {
            var size = new FileInfo(_paths.DatabasePath).Length;
            if (size >= DbSizeWarningThresholdBytes)
            {
                var sizeMb = Math.Round(size / 1024d / 1024d);
                warnings.Add(new WarningItem("db-size", $"Database size is {sizeMb} MB. Consider archiving older data."));
            }
        }

        if (Directory.Exists(_paths.AttachmentsDirectory))
        {
            var attachmentCount = Directory.EnumerateFiles(_paths.AttachmentsDirectory).Count();
            if (attachmentCount >= AttachmentWarningThreshold)
            {
                warnings.Add(new WarningItem("attachments", $"Large number of attachments detected ({attachmentCount})."));
            }
        }
        return warnings;
    }

    public async Task<MaintenanceStatus> GetStatusAsync()
    {
        var state = await _stateStore.LoadAsync();
        return new MaintenanceStatus(
            state.LastBackupAt,
            state.LastBackupError,
            state.LastReindexAt,
            state.MirrorBackupError,
            state.BackupVerificationError,
            _startupState.Info.RecoveryMode);
    }

    public async Task ReindexSearchAsync(CancellationToken token)
    {
        if (!_startupState.Info.Healthy)
        {
            throw new InvalidOperationException("Search cannot be reindexed while Glance is in recovery mode.");
        }
        var state = await _stateStore.LoadAsync();
        if (state.ReindexInProgress) _logger.LogWarning("Search reindex already in progress");
        state.ReindexInProgress = true;
        await _stateStore.SaveAsync(state);
        try
        {
            await _tasks.RebuildSearchIndexAsync(token);
            state.LastReindexAt = TimeProvider.Now.ToString("yyyy-MM-dd HH:mm:ss");
            await _stateStore.SaveAsync(state);
        }
        finally
        {
            state.ReindexInProgress = false;
            await _stateStore.SaveAsync(state);
        }
    }

    private async Task EnsureSearchIndexAsync(CancellationToken token)
    {
        var state = await _stateStore.LoadAsync();
        var missing = !await _tasks.SearchIndexExistsAsync(token);
        if (!missing && !state.ReindexInProgress) return;
        _logger.LogInformation("Rebuilding search index");
        await ReindexSearchAsync(token);
    }
}
