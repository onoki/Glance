namespace Glance.Server;

internal sealed class DatabaseStartupCoordinator
{
    private readonly AppPaths _paths;
    private readonly DatabaseInitializer _initializer;
    private readonly DatabaseHealthService _health;
    private readonly DataSafetyService _dataSafety;
    private readonly RestoreCoordinator _restore;
    private readonly MaintenanceStateStore _stateStore;
    private readonly ILogger<DatabaseStartupCoordinator> _logger;

    public DatabaseStartupCoordinator(
        AppPaths paths,
        DatabaseInitializer initializer,
        DatabaseHealthService health,
        DataSafetyService dataSafety,
        RestoreCoordinator restore,
        MaintenanceStateStore stateStore,
        ILogger<DatabaseStartupCoordinator> logger)
    {
        _paths = paths;
        _initializer = initializer;
        _health = health;
        _dataSafety = dataSafety;
        _restore = restore;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<StartupSafetyInfo> PrepareAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        Directory.CreateDirectory(_paths.BackupsDirectory);
        Directory.CreateDirectory(_paths.RecoveryDirectory);

        var restore = await _restore.ApplyPendingRestoreAsync(token);
        var supported = _health.GetSupportedSchemaVersion();
        if (restore.Failed)
        {
            await _stateStore.SetIntegrityErrorAsync(true);
            return new StartupSafetyInfo(false, true, restore.Message, null, supported, false);
        }

        if (!File.Exists(_paths.DatabasePath))
        {
            var orphanedSidecarExists = File.Exists(_paths.DatabasePath + "-wal")
                || File.Exists(_paths.DatabasePath + "-shm");
            if (orphanedSidecarExists || await _dataSafety.HasAnyBackupAsync(token))
            {
                var message = orphanedSidecarExists
                    ? "The live database is missing, but SQLite recovery files remain. Glance did not create an empty replacement; restore a verified backup or preserve the data folder for investigation."
                    : "The live database is missing, but backups exist. Glance did not create an empty replacement; select a backup to restore.";
                await _stateStore.SetIntegrityErrorAsync(true);
                return new StartupSafetyInfo(false, true, message, null, supported, restore.Applied);
            }

            try
            {
                _initializer.Initialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fresh database creation failed");
                await _stateStore.SetIntegrityErrorAsync(true);
                return new StartupSafetyInfo(false, true, $"Unable to create the database: {ex.Message}", null, supported, restore.Applied);
            }

            var freshHealth = await _health.CheckLiveDatabaseAsync(token);
            await _stateStore.SetIntegrityErrorAsync(!freshHealth.IsHealthy);
            return ToStartupInfo(freshHealth, restore.Applied, "The newly created database failed verification.");
        }

        // This read-only check intentionally happens before any migration or other
        // startup write. A failed check leaves the original DB/WAL/SHM untouched.
        var before = await _health.CheckLiveDatabaseAsync(token);
        if (!before.IsHealthy)
        {
            await _stateStore.SetIntegrityErrorAsync(true);
            return ToStartupInfo(before, restore.Applied, "The database failed its startup safety check. Glance is in recovery mode and did not rebuild or replace it.");
        }

        if (before.SchemaVersion < before.SupportedSchemaVersion)
        {
            var backup = await _dataSafety.CreateBackupAsync("pre-migration", TimeProvider.Now, token);
            if (!backup.Success)
            {
                await _stateStore.SetIntegrityErrorAsync(true);
                return new StartupSafetyInfo(
                    false,
                    true,
                    $"A required pre-migration backup could not be verified, so no migration was attempted: {backup.Error}",
                    before.SchemaVersion,
                    before.SupportedSchemaVersion,
                    restore.Applied);
            }
        }

        try
        {
            _initializer.Initialize();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed; the verified pre-migration backup was retained");
            await _stateStore.SetIntegrityErrorAsync(true);
            return new StartupSafetyInfo(
                false,
                true,
                $"Database migration failed. The verified pre-migration backup was retained and Glance will not continue writing: {ex.Message}",
                before.SchemaVersion,
                before.SupportedSchemaVersion,
                restore.Applied);
        }

        var after = await _health.CheckLiveDatabaseAsync(token);
        await _stateStore.SetIntegrityErrorAsync(!after.IsHealthy);
        return ToStartupInfo(after, restore.Applied, "The database failed verification after migration. Glance will not continue writing.");
    }

    private static StartupSafetyInfo ToStartupInfo(
        DatabaseHealthResult health,
        bool restoreApplied,
        string failurePrefix)
    {
        var message = health.IsHealthy
            ? restoreApplied ? "The selected backup was restored and verified." : null
            : $"{failurePrefix} {string.Join(" ", health.Errors)}";
        return new StartupSafetyInfo(
            health.IsHealthy,
            !health.IsHealthy,
            message,
            health.SchemaVersion,
            health.SupportedSchemaVersion,
            restoreApplied);
    }
}
