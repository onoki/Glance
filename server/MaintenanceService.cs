using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

internal sealed class MaintenanceService
{
    private readonly AppPaths _paths;
    private readonly ILogger<MaintenanceService> _logger;
    private readonly TaskRepository _tasks;
    private readonly MaintenanceStateStore _stateStore;
    private readonly AttachmentMaintenance _attachmentMaintenance;

    private const int TaskWarningThreshold = 5000;
    private const int AttachmentWarningThreshold = 1000;
    private const long DbSizeWarningThresholdBytes = 200L * 1024 * 1024;

    public MaintenanceService(
        AppPaths paths,
        ILogger<MaintenanceService> logger,
        TaskRepository tasks,
        MaintenanceStateStore stateStore,
        AttachmentMaintenance attachmentMaintenance)
    {
        _paths = paths;
        _logger = logger;
        _tasks = tasks;
        _stateStore = stateStore;
        _attachmentMaintenance = attachmentMaintenance;
    }

    public async Task<bool> RunIntegrityCheckAsync(CancellationToken token)
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            return true;
        }

        try
        {
            await using var connection = new SqliteConnection($"{_paths.ConnectionString};Mode=ReadOnly");
            await connection.OpenAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = await command.ExecuteScalarAsync(token);
            var message = result?.ToString() ?? string.Empty;

            if (string.Equals(message, "ok", StringComparison.OrdinalIgnoreCase))
            {
                await _stateStore.SetIntegrityErrorAsync(false);
                return true;
            }

            _logger.LogError("Integrity check failed: {Message}", message);
            var recovered = await TryRecoverDatabaseAsync(token);
            await _stateStore.SetIntegrityErrorAsync(!recovered);
            return recovered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integrity check failed");
            await _stateStore.SetIntegrityErrorAsync(true);
            return false;
        }
    }

    public async Task<bool> CreateBackupAsync(DateTime localNow, CancellationToken token)
    {
        var timestamp = localNow.ToString("yyyy-MM-dd-HHmmss");
        var backupFolder = Path.Combine(_paths.BackupsDirectory, timestamp);
        Directory.CreateDirectory(backupFolder);

        var tempSuffix = Guid.NewGuid().ToString("N")[..8];
        var backupDbPath = Path.Combine(backupFolder, $"glance_{tempSuffix}.db");
        var zipPath = Path.Combine(backupFolder, $"glance_backup_{timestamp}.zip");
        if (File.Exists(zipPath))
        {
            zipPath = Path.Combine(backupFolder, $"glance_backup_{timestamp}_{tempSuffix}.zip");
        }

        try
        {
            await BackupDatabaseAsync(backupDbPath, token);
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(backupDbPath, Path.Combine("data", "glance.db"));

                if (Directory.Exists(_paths.AttachmentsDirectory))
                {
                    foreach (var file in Directory.GetFiles(_paths.AttachmentsDirectory))
                    {
                        var name = Path.GetFileName(file);
                        archive.CreateEntryFromFile(file, Path.Combine("blobs", "attachments", name));
                    }
                }
            }

            File.Delete(backupDbPath);
            _logger.LogInformation("Backup created at {BackupPath}", zipPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed");
            await _stateStore.RecordBackupFailureAsync(ex.Message);
            return false;
        }
    }

    public Task RecordBackupSuccessAsync(DateTime localNow, string? dayKeyOverride = null)
    {
        return _stateStore.RecordBackupSuccessAsync(localNow, dayKeyOverride);
    }

    public async Task RunStartupMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        await EnsureSearchIndexAsync(token);
        await RunAutomaticBackupIfNewDayAsync(localNow, token);
        _ = Task.Run(() => _attachmentMaintenance.RunAttachmentGcAsync(token), token);
    }

    public async Task RunDailyMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        await RunAutomaticBackupIfNewDayAsync(localNow, token);
        await EnsureSearchIndexAsync(token);
        _ = Task.Run(() => _attachmentMaintenance.RunAttachmentGcAsync(token), token);
    }

    public async Task<IReadOnlyList<WarningItem>> GetWarningsAsync(CancellationToken token)
    {
        var warnings = new List<WarningItem>();
        var state = await _stateStore.LoadAsync();

        if (state.IntegrityError)
        {
            warnings.Add(new WarningItem("integrity", "Database integrity check failed. Some data may be corrupted."));
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
        return new MaintenanceStatus(state.LastBackupAt, state.LastBackupError, state.LastReindexAt);
    }

    public async Task ReindexSearchAsync(CancellationToken token)
    {
        var state = await _stateStore.LoadAsync();
        if (state.ReindexInProgress)
        {
            _logger.LogWarning("Search reindex already in progress");
        }

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
        if (!missing && !state.ReindexInProgress)
        {
            return;
        }

        _logger.LogInformation("Rebuilding search index");
        await ReindexSearchAsync(token);
    }

    private async Task RunAutomaticBackupIfNewDayAsync(DateTime localNow, CancellationToken token)
    {
        var state = await _stateStore.LoadAsync();
        var dayKey = localNow.ToString("yyyy-MM-dd");
        if (string.Equals(state.LastBackupDay, dayKey, StringComparison.Ordinal))
        {
            return;
        }

        var success = await CreateBackupAsync(localNow, token);
        if (success)
        {
            await _stateStore.RecordBackupSuccessAsync(localNow, dayKey);
        }
    }

        private async Task BackupDatabaseAsync(string backupPath, CancellationToken token)
    {
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        var destinationConnectionString = $"Data Source={backupPath};Mode=ReadWriteCreate;Cache=Shared;Pooling=False";
        await using var source = new SqliteConnection(_paths.ConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(token);
        await destination.OpenAsync(token);

        source.BackupDatabase(destination);
        await destination.CloseAsync();
        await source.CloseAsync();
    }

    private async Task<bool> TryRecoverDatabaseAsync(CancellationToken token)
    {
        try
        {
            var timestamp = TimeProvider.Now.ToString("yyyyMMddHHmmss");
            var recoveredPath = Path.Combine(_paths.DataDirectory, $"glance_recovered_{timestamp}.db");
            await using (var connection = new SqliteConnection(_paths.ConnectionString))
            {
                await connection.OpenAsync(token);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path;";
                command.Parameters.AddWithValue("$path", recoveredPath);
                await command.ExecuteNonQueryAsync(token);
            }

            if (!File.Exists(recoveredPath))
            {
                return false;
            }

            var corruptPath = Path.Combine(_paths.DataDirectory, $"glance_corrupt_{timestamp}.db");
            File.Move(_paths.DatabasePath, corruptPath, true);
            File.Move(recoveredPath, _paths.DatabasePath, true);

            _logger.LogWarning("Recovered database to {RecoveredPath}. Original saved at {CorruptPath}", _paths.DatabasePath, corruptPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database recovery failed");
            return false;
        }
    }
}
