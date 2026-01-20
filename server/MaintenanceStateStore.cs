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
            var json = await File.ReadAllTextAsync(_statePath);
            return JsonSerializer.Deserialize<MaintenanceState>(json) ?? new MaintenanceState();
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
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_statePath, json);
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

    public async Task RecordBackupSuccessAsync(DateTime localNow, string? dayKeyOverride = null)
    {
        var state = await LoadAsync();
        state.LastBackupDay = dayKeyOverride ?? localNow.ToString("yyyy-MM-dd");
        state.LastBackupAt = localNow.ToString("yyyy-MM-dd HH:mm:ss");
        state.LastBackupError = null;
        await SaveAsync(state);
    }

    public async Task RecordBackupFailureAsync(string message)
    {
        var state = await LoadAsync();
        state.LastBackupError = message;
        await SaveAsync(state);
    }
}

internal sealed class MaintenanceState
{
    public string? LastBackupDay { get; set; }
    public string? LastBackupAt { get; set; }
    public string? LastBackupError { get; set; }
    public string? LastReindexAt { get; set; }
    public bool ReindexInProgress { get; set; }
    public bool IntegrityError { get; set; }
}
