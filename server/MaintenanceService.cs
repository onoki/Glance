using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed class MaintenanceService
{
    private readonly AppPaths _paths;
    private readonly ILogger<MaintenanceService> _logger;
    private readonly TaskRepository _tasks;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly string _statePath;

    private const int TaskWarningThreshold = 5000;
    private const int AttachmentWarningThreshold = 1000;
    private const long DbSizeWarningThresholdBytes = 200L * 1024 * 1024;

    public MaintenanceService(AppPaths paths, ILogger<MaintenanceService> logger, TaskRepository tasks)
    {
        _paths = paths;
        _logger = logger;
        _tasks = tasks;
        _statePath = Path.Combine(paths.DataDirectory, "maintenance.json");
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
                await SetIntegrityErrorAsync(false);
                return true;
            }

            _logger.LogError("Integrity check failed: {Message}", message);
            var recovered = await TryRecoverDatabaseAsync(token);
            await SetIntegrityErrorAsync(!recovered);
            return recovered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integrity check failed");
            await SetIntegrityErrorAsync(true);
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
            await RecordBackupFailureAsync(ex.Message);
            return false;
        }
    }

    public async Task RunStartupMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        await EnsureSearchIndexAsync(token);
        await RunAutomaticBackupIfNewDayAsync(localNow, token);
        _ = Task.Run(() => RunAttachmentGcAsync(token), token);
    }

    public async Task RunDailyMaintenanceAsync(DateTime localNow, CancellationToken token)
    {
        await RunAutomaticBackupIfNewDayAsync(localNow, token);
        await EnsureSearchIndexAsync(token);
        _ = Task.Run(() => RunAttachmentGcAsync(token), token);
    }

    public async Task<IReadOnlyList<WarningItem>> GetWarningsAsync(CancellationToken token)
    {
        var warnings = new List<WarningItem>();
        var state = await LoadStateAsync();

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
        var state = await LoadStateAsync();
        return new MaintenanceStatus(state.LastBackupAt, state.LastBackupError, state.LastReindexAt);
    }

    public async Task ReindexSearchAsync(CancellationToken token)
    {
        var state = await LoadStateAsync();
        if (state.ReindexInProgress)
        {
            _logger.LogWarning("Search reindex already in progress");
        }

        state.ReindexInProgress = true;
        await SaveStateAsync(state);

        try
        {
            await _tasks.RebuildSearchIndexAsync(token);
            state.LastReindexAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            await SaveStateAsync(state);
        }
        finally
        {
            state.ReindexInProgress = false;
            await SaveStateAsync(state);
        }
    }

    private async Task EnsureSearchIndexAsync(CancellationToken token)
    {
        var state = await LoadStateAsync();
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
        var state = await LoadStateAsync();
        var dayKey = localNow.ToString("yyyy-MM-dd");
        if (string.Equals(state.LastBackupDay, dayKey, StringComparison.Ordinal))
        {
            return;
        }

        var success = await CreateBackupAsync(localNow, token);
        if (success)
        {
            await RecordBackupSuccessAsync(localNow, dayKey);
        }
    }

    private async Task<int> RunAttachmentGcAsync(CancellationToken token)
    {
        try
        {
            var referenced = await CollectReferencedAttachmentsAsync(token);
            if (!Directory.Exists(_paths.AttachmentsDirectory))
            {
                return 0;
            }

            var deleted = 0;
            foreach (var file in Directory.GetFiles(_paths.AttachmentsDirectory))
            {
                var name = Path.GetFileName(file);
                if (!referenced.Contains(name))
                {
                    File.Delete(file);
                    deleted += 1;
                    _logger.LogInformation("Deleted orphaned attachment {File}", name);
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment GC failed");
            return 0;
        }
    }

    private async Task<HashSet<string>> CollectReferencedAttachmentsAsync(CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT content_json FROM tasks;";

        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var json = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }
            using var doc = JsonDocument.Parse(json);
            CollectAttachmentRefs(doc.RootElement, result);
        }

        return result;
    }

    private static void CollectAttachmentRefs(JsonElement element, HashSet<string> refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("type", out var typeElement)
                    && typeElement.ValueKind == JsonValueKind.String
                    && typeElement.GetString() == "image"
                    && element.TryGetProperty("attrs", out var attrs)
                    && attrs.ValueKind == JsonValueKind.Object
                    && attrs.TryGetProperty("src", out var srcElement)
                    && srcElement.ValueKind == JsonValueKind.String)
                {
                    var fileName = ExtractAttachmentFileName(srcElement.GetString());
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        refs.Add(fileName);
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectAttachmentRefs(property.Value, refs);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectAttachmentRefs(child, refs);
                }
                break;
        }
    }

    private static string? ExtractAttachmentFileName(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }
        var index = src.IndexOf("/attachments/", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var path = src[(index + "/attachments/".Length)..];
            path = path.Split('?', '#')[0];
            return Path.GetFileName(path);
        }
        return null;
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
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
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

    private async Task<MaintenanceState> LoadStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!File.Exists(_statePath))
            {
                return new MaintenanceState();
            }
            var json = await File.ReadAllTextAsync(_statePath);
            return JsonSerializer.Deserialize<MaintenanceState>(json) ?? new MaintenanceState();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task SaveStateAsync(MaintenanceState state)
    {
        await _stateLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_statePath, json);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task SetIntegrityErrorAsync(bool value)
    {
        var state = await LoadStateAsync();
        state.IntegrityError = value;
        await SaveStateAsync(state);
    }

    public async Task RecordBackupSuccessAsync(DateTime localNow, string? dayKeyOverride = null)
    {
        var state = await LoadStateAsync();
        state.LastBackupDay = dayKeyOverride ?? localNow.ToString("yyyy-MM-dd");
        state.LastBackupAt = localNow.ToString("yyyy-MM-dd HH:mm:ss");
        state.LastBackupError = null;
        await SaveStateAsync(state);
    }

    private async Task RecordBackupFailureAsync(string message)
    {
        var state = await LoadStateAsync();
        state.LastBackupError = message;
        await SaveStateAsync(state);
    }

    private sealed class MaintenanceState
    {
        public string? LastBackupDay { get; set; }
        public string? LastBackupAt { get; set; }
        public string? LastBackupError { get; set; }
        public string? LastReindexAt { get; set; }
        public bool ReindexInProgress { get; set; }
        public bool IntegrityError { get; set; }
    }
}
