using System.Text.Json;

namespace Glance.Server;

internal sealed class MaintenanceStateStore
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly string _statePath;

    public MaintenanceStateStore(AppPaths paths)
    {
        _statePath = Path.Combine(paths.DataDirectory, "maintenance.json");
    }

    public async Task<MaintenanceState> LoadAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!File.Exists(_statePath))
            {
                return new MaintenanceState();
            }
            try
            {
                var json = await File.ReadAllTextAsync(_statePath);
                return JsonSerializer.Deserialize<MaintenanceState>(json) ?? new MaintenanceState();
            }
            catch (JsonException)
            {
                // Maintenance metadata is derived bookkeeping, not user notes. A
                // malformed file must not prevent Glance from checking the DB or
                // creating a new verified backup.
                return new MaintenanceState();
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task SaveAsync(MaintenanceState state)
    {
        await _stateLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _statePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _statePath, true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task SetIntegrityErrorAsync(bool value)
    {
        var state = await LoadAsync();
        state.IntegrityError = value;
        await SaveAsync(state);
    }

    public async Task RecordBackupSuccessAsync(DateTime localNow, string? dayKeyOverride = null, long? lastChangeId = null)
    {
        var state = await LoadAsync();
        state.LastBackupDay = dayKeyOverride ?? localNow.ToString("yyyy-MM-dd");
        state.LastBackupAt = localNow.ToString("yyyy-MM-dd HH:mm:ss");
        state.LastBackupAtUtc = DateTimeOffset.UtcNow;
        if (lastChangeId.HasValue)
        {
            state.LastBackupChangeId = lastChangeId.Value;
        }
        state.LastBackupError = null;
        await SaveAsync(state);
    }

    public async Task RecordBackupFailureAsync(string message)
    {
        var state = await LoadAsync();
        state.LastBackupError = message;
        await SaveAsync(state);
    }

    public async Task SetMirrorBackupErrorAsync(string? message)
    {
        var state = await LoadAsync();
        state.MirrorBackupError = message;
        await SaveAsync(state);
    }

    public async Task RecordBackupVerificationAsync(DateTimeOffset verifiedAtUtc, string? error)
    {
        var state = await LoadAsync();
        state.LastBackupVerificationAtUtc = verifiedAtUtc;
        state.BackupVerificationError = error;
        await SaveAsync(state);
    }
}

internal sealed class MaintenanceState
{
    public string? LastBackupDay { get; set; }
    public string? LastBackupAt { get; set; }
    public DateTimeOffset? LastBackupAtUtc { get; set; }
    public long LastBackupChangeId { get; set; }
    public string? LastBackupError { get; set; }
    public string? MirrorBackupError { get; set; }
    public DateTimeOffset? LastBackupVerificationAtUtc { get; set; }
    public string? BackupVerificationError { get; set; }
    public string? LastReindexAt { get; set; }
    public bool ReindexInProgress { get; set; }
    public bool IntegrityError { get; set; }
}
