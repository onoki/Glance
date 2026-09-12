using System.Text.Json;

namespace Glance.Server;

internal sealed class DataSafetySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DataSafetySettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<DataSafetySettings> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(_paths.DataSafetySettingsPath))
            {
                return new DataSafetySettings();
            }

            await using var stream = File.OpenRead(_paths.DataSafetySettingsPath);
            return await JsonSerializer.DeserializeAsync<DataSafetySettings>(stream, JsonOptions, token)
                ?? new DataSafetySettings();
        }
        catch (JsonException)
        {
            // A malformed optional settings file must not prevent access to notes.
            return new DataSafetySettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(DataSafetySettings settings, CancellationToken token = default)
    {
        Validate(settings);
        await _gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            var tempPath = _paths.DataSafetySettingsPath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, token);
                await stream.FlushAsync(token);
            }
            File.Move(tempPath, _paths.DataSafetySettingsPath, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Validate(DataSafetySettings settings)
    {
        if (settings.HourlyRetentionCount is < 1 or > 744)
        {
            throw new ArgumentException("Hourly retention must be between 1 and 744 backups.");
        }
        if (settings.DailyRetentionDays is < 1 or > 3660)
        {
            throw new ArgumentException("Daily retention must be between 1 and 3660 days.");
        }
        if (settings.MonthlyRetentionMonths is < 1 or > 120)
        {
            throw new ArgumentException("Monthly retention must be between 1 and 120 months.");
        }
        if (!string.IsNullOrWhiteSpace(settings.MirrorDirectory) && !Path.IsPathFullyQualified(settings.MirrorDirectory))
        {
            throw new ArgumentException("The backup location must be an absolute path.");
        }
    }
}
