using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

internal sealed class DataSafetyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly DatabaseHealthService _health;
    private readonly DataSafetySettingsStore _settingsStore;
    private readonly MaintenanceStateStore _stateStore;
    private readonly TaskRepository _tasks;
    private readonly AttachmentMaintenance _attachmentMaintenance;
    private readonly ILogger<DataSafetyService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, SessionVerificationOutcome> _sessionVerifications = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _statusSchemaGate = new(1, 1);
    private JsonSchema? _statusSchema;

    public DataSafetyService(
        AppPaths paths,
        DatabaseHealthService health,
        DataSafetySettingsStore settingsStore,
        MaintenanceStateStore stateStore,
        TaskRepository tasks,
        AttachmentMaintenance attachmentMaintenance,
        ILogger<DataSafetyService> logger)
    {
        _paths = paths;
        _health = health;
        _settingsStore = settingsStore;
        _stateStore = stateStore;
        _tasks = tasks;
        _attachmentMaintenance = attachmentMaintenance;
        _logger = logger;
    }

    public Task<DataSafetySettings> GetSettingsAsync(CancellationToken token) =>
        _settingsStore.LoadAsync(token);

    public async Task<DataSafetySettings> SaveSettingsAsync(DataSafetySettings settings, CancellationToken token)
    {
        var normalized = new DataSafetySettings
        {
            HourlyBackupsEnabled = settings.HourlyBackupsEnabled,
            MirrorDirectory = string.IsNullOrWhiteSpace(settings.MirrorDirectory)
                ? null
                : Path.GetFullPath(settings.MirrorDirectory.Trim()),
            HourlyRetentionCount = settings.HourlyRetentionCount,
            DailyRetentionDays = settings.DailyRetentionDays,
            MonthlyRetentionMonths = settings.MonthlyRetentionMonths
        };

        if (normalized.MirrorDirectory is not null && IsWithin(normalized.MirrorDirectory, _paths.AppRoot))
        {
            throw new ArgumentException("Choose a backup location outside the Glance application folder.");
        }

        await _settingsStore.SaveAsync(normalized, token);
        return normalized;
    }

    public async Task<DataSafetyLocationTestResult> TestLocationAsync(string? location, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return new DataSafetyLocationTestResult(false, false, "Choose an absolute folder first.");
        }
        if (!Path.IsPathFullyQualified(location))
        {
            return new DataSafetyLocationTestResult(false, false, "The backup location must be an absolute path.");
        }

        var fullPath = Path.GetFullPath(location);
        if (IsWithin(fullPath, _paths.AppRoot))
        {
            return new DataSafetyLocationTestResult(
                Directory.Exists(fullPath),
                false,
                "Choose a backup location outside the Glance application folder.");
        }

        var testPath = Path.Combine(fullPath, $".glance-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(fullPath);
            await File.WriteAllTextAsync(testPath, "Glance backup write test", token);
            await using (var stream = new FileStream(testPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _ = stream.Length;
            }
            File.Delete(testPath);
            return new DataSafetyLocationTestResult(true, true, "The location is available and writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(testPath);
            return new DataSafetyLocationTestResult(Directory.Exists(fullPath), false, ex.Message);
        }
    }

    public async Task<BackupCreationResult> CreateBackupAsync(
        string reason,
        DateTime localNow,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await CreateBackupUnderGateAsync(reason, localNow, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupCatalogItem>> GetCatalogAsync(CancellationToken token)
    {
        var descriptors = await FindArchivesAsync(token);
        return descriptors
            .GroupBy(item => item.BackupId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group.FirstOrDefault(item => item.IsLocal) ?? group.First();
                var hasSessionOutcome = _sessionVerifications.TryGetValue(preferred.BackupId, out var sessionOutcome);
                var creationVerifiedAt = preferred.Manifest?.VerifiedAtUtc;
                return new BackupCatalogItem(
                    preferred.BackupId,
                    preferred.CreatedAtUtc,
                    preferred.CreatedAtLocal,
                    preferred.Reason,
                    preferred.Format,
                    preferred.Manifest?.SchemaVersion,
                    preferred.Size,
                    preferred.Manifest is null
                        ? new BackupCatalogCounts(null, null, null, null)
                        : new BackupCatalogCounts(
                            preferred.Manifest.Counts.Tasks,
                            preferred.Manifest.Counts.People,
                            preferred.Manifest.Counts.Attachments,
                            preferred.Manifest.Counts.StatusUpdates),
                    hasSessionOutcome && !sessionOutcome!.IsValid
                        ? "failed"
                        : hasSessionOutcome || preferred.Manifest is not null ? "verified" : "not-verified",
                    hasSessionOutcome
                        ? sessionOutcome!.IsValid ? sessionOutcome.CheckedAtUtc : null
                        : creationVerifiedAt,
                    group.Any(item => item.IsLocal),
                    group.Any(item => !item.IsLocal));
            })
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
    }

    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupId, CancellationToken token)
    {
        var descriptors = await ResolveArchivesAsync(backupId, token);
        if (descriptors.Count == 0)
        {
            throw new KeyNotFoundException("The selected backup is no longer available.");
        }

        var failures = new List<string>();
        for (var index = 0; index < descriptors.Count; index += 1)
        {
            var descriptor = descriptors[index];
            var result = await VerifyArchiveAsync(descriptor.Path, token);
            if (result.IsValid)
            {
                if (failures.Count > 0)
                {
                    _logger.LogWarning(
                        "Backup {BackupId} used {CopyLabel} because preferred copies failed verification: {Errors}",
                        backupId,
                        DescribeCopy(descriptors, index),
                        string.Join(" ", failures));
                }
                RecordSessionVerification(backupId, true, result.VerifiedAtUtc);
                return result;
            }
            failures.Add($"{DescribeCopy(descriptors, index)}: {string.Join(" ", result.Errors)}");
        }

        var preferred = descriptors[0];
        var failedAt = DateTimeOffset.UtcNow;
        RecordSessionVerification(backupId, false, failedAt);
        return new BackupVerificationResult(
            false,
            backupId,
            preferred.Format,
            preferred.CreatedAtUtc,
            preferred.Manifest?.SchemaVersion,
            failedAt,
            failures);
    }

    internal Task<BackupVerificationResult> VerifyBackupFileAsync(string path, CancellationToken token) =>
        VerifyArchiveAsync(path, token);

    public async Task<BackupRestorePlanResponse> StageRestoreAsync(
        string backupId,
        bool createEmergencyBackup,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (File.Exists(_paths.PendingRestorePath))
            {
                throw new InvalidOperationException("A restore is already waiting for an application restart.");
            }

            var descriptors = await ResolveArchivesAsync(backupId, token);
            if (descriptors.Count == 0)
            {
                throw new KeyNotFoundException("The selected backup is no longer available.");
            }

            ArchiveDescriptor? descriptor = null;
            var verificationFailures = new List<string>();
            for (var index = 0; index < descriptors.Count; index += 1)
            {
                var candidate = descriptors[index];
                var candidateVerification = await VerifyArchiveAsync(candidate.Path, token);
                if (candidateVerification.IsValid)
                {
                    descriptor = candidate;
                    RecordSessionVerification(backupId, true, candidateVerification.VerifiedAtUtc);
                    if (verificationFailures.Count > 0)
                    {
                        _logger.LogWarning(
                            "Restore for backup {BackupId} used {CopyLabel} because preferred copies failed verification: {Errors}",
                            backupId,
                            DescribeCopy(descriptors, index),
                            string.Join(" ", verificationFailures));
                    }
                    break;
                }
                verificationFailures.Add($"{DescribeCopy(descriptors, index)}: {string.Join(" ", candidateVerification.Errors)}");
            }
            if (descriptor is null)
            {
                RecordSessionVerification(backupId, false, DateTimeOffset.UtcNow);
                throw new InvalidDataException(
                    $"Every available copy of the selected backup failed verification. {string.Join(" ", verificationFailures)}");
            }

            string? emergencyBackupId = null;
            if (createEmergencyBackup && File.Exists(_paths.DatabasePath))
            {
                var emergency = await CreateBackupUnderGateAsync("pre-restore", TimeProvider.Now, token);
                if (!emergency.Success)
                {
                    throw new InvalidOperationException($"The emergency pre-restore backup failed: {emergency.Error}");
                }
                emergencyBackupId = emergency.BackupId;
            }

            var restoreId = Guid.NewGuid().ToString("N");
            var stagingDirectory = Path.Combine(_paths.RestoreStagingDirectory, restoreId);
            Directory.CreateDirectory(stagingDirectory);
            var stagedEntries = await ExtractRestorableEntriesAsync(descriptor.Path, stagingDirectory, token);

            var plan = new PendingRestorePlan
            {
                RestoreId = restoreId,
                BackupId = backupId,
                StagingDirectory = stagingDirectory,
                RescueDirectory = Path.Combine(_paths.RecoveryDirectory, $"pre-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{restoreId[..8]}"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Entries = stagedEntries.ToList()
            };
            await WriteJsonAtomicallyAsync(_paths.PendingRestorePath, plan, token);

            return new BackupRestorePlanResponse(
                true,
                backupId,
                "Restore is staged and verified. Restart Glance to apply it.",
                true,
                emergencyBackupId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunRetentionAsync(bool includeMirror, CancellationToken token)
    {
        var settings = await _settingsStore.LoadAsync(token);
        ApplyRetention(_paths.BackupsDirectory, settings);
        if (includeMirror
            && !string.IsNullOrWhiteSpace(settings.MirrorDirectory)
            && Directory.Exists(settings.MirrorDirectory))
        {
            ApplyRetention(settings.MirrorDirectory, settings);
        }
    }

    public async Task ReverifyNewestIfDueAsync(CancellationToken token)
    {
        var state = await _stateStore.LoadAsync();
        if (state.LastBackupVerificationAtUtc.HasValue
            && DateTimeOffset.UtcNow - state.LastBackupVerificationAtUtc.Value < TimeSpan.FromDays(7))
        {
            return;
        }

        var archives = await FindArchivesAsync(token);
        var candidates = new List<ArchiveDescriptor>();
        var newestLocal = archives.Where(item => item.IsLocal).MaxBy(item => item.CreatedAtUtc);
        var newestMirror = archives.Where(item => !item.IsLocal).MaxBy(item => item.CreatedAtUtc);
        if (newestLocal is not null) candidates.Add(newestLocal);
        if (newestMirror is not null
            && candidates.All(item => !PathsEqual(item.Path, newestMirror.Path)))
        {
            candidates.Add(newestMirror);
        }
        if (candidates.Count == 0) return;

        var failures = new List<string>();
        foreach (var backupId in candidates.Select(item => item.BackupId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var copies = await ResolveArchivesAsync(backupId, token);
            var anyValid = false;
            DateTimeOffset? latestSuccess = null;
            for (var index = 0; index < copies.Count; index += 1)
            {
                var copy = copies[index];
                var verification = await VerifyArchiveAsync(copy.Path, token);
                if (verification.IsValid)
                {
                    anyValid = true;
                    if (!latestSuccess.HasValue || verification.VerifiedAtUtc > latestSuccess.Value)
                    {
                        latestSuccess = verification.VerifiedAtUtc;
                    }
                }
                else
                {
                    failures.Add($"{DescribeCopy(copies, index)} of backup {backupId}: {string.Join(" ", verification.Errors)}");
                }
            }
            if (anyValid)
            {
                RecordSessionVerification(backupId, true, latestSuccess ?? DateTimeOffset.UtcNow);
            }
            else
            {
                RecordSessionVerification(backupId, false, DateTimeOffset.UtcNow);
            }
        }

        var now = DateTimeOffset.UtcNow;
        await _stateStore.RecordBackupVerificationAsync(now, failures.Count == 0 ? null : string.Join(" ", failures));
    }

    public async Task<long> GetLastChangeIdAsync(CancellationToken token)
    {
        if (!File.Exists(_paths.DatabasePath)) return 0;
        await using var connection = new SqliteConnection(_paths.ReadOnlyConnectionString);
        await connection.OpenAsync(token);
        if (!await DatabaseHealthService.TableExistsAsync(connection, "changes", token)) return 0;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM changes;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }

    public async Task<bool> HasAnyBackupAsync(CancellationToken token)
    {
        if (Directory.Exists(_paths.BackupsDirectory)
            && Directory.EnumerateFiles(_paths.BackupsDirectory, "*.zip", SearchOption.AllDirectories).Any())
        {
            return true;
        }
        var settings = await _settingsStore.LoadAsync(token);
        return !string.IsNullOrWhiteSpace(settings.MirrorDirectory)
            && Directory.Exists(settings.MirrorDirectory)
            && Directory.EnumerateFiles(settings.MirrorDirectory, "*.zip", SearchOption.AllDirectories).Any();
    }

    private async Task<BackupCreationResult> CreateBackupUnderGateAsync(
        string reason,
        DateTime localNow,
        CancellationToken token)
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            return BackupCreationResult.Failed("The database does not exist.");
        }

        var backupId = Guid.NewGuid().ToString("N");
        var workDirectory = Path.Combine(_paths.BackupWorkingDirectory, backupId);
        var packageDirectory = Path.Combine(workDirectory, "package");
        var timestamp = localNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var targetDirectory = Path.Combine(_paths.BackupsDirectory, localNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var fileName = $"glance-backup-{timestamp}-{backupId[..8]}.zip";
        var finalPath = Path.Combine(targetDirectory, fileName);
        var partialPath = finalPath + ".partial";

        try
        {
            Directory.CreateDirectory(packageDirectory);
            Directory.CreateDirectory(targetDirectory);
            var packageDbPath = Path.Combine(packageDirectory, "data", "glance.db");
            Directory.CreateDirectory(Path.GetDirectoryName(packageDbPath)!);
            await BackupDatabaseAsync(packageDbPath, token);

            await CopyBackupResourcesAsync(packageDirectory, token);
            var health = await _health.CheckDatabaseAsync(packageDbPath, token);
            if (!health.IsHealthy)
            {
                throw new InvalidDataException($"The database snapshot failed verification: {string.Join(" ", health.Errors)}");
            }

            var files = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new PackageFile(path, NormalizeRelativePath(Path.GetRelativePath(packageDirectory, path))))
                .OrderBy(file => file.ArchivePath, StringComparer.Ordinal)
                .ToList();
            var entries = new List<BackupManifestEntry>(files.Count);
            foreach (var file in files)
            {
                entries.Add(new BackupManifestEntry(
                    file.ArchivePath,
                    new FileInfo(file.DiskPath).Length,
                    await ComputeSha256Async(file.DiskPath, token)));
            }

            var counts = await ReadCountsAsync(packageDbPath, entries, token);
            var nowUtc = DateTimeOffset.UtcNow;
            var manifest = new BackupManifest(
                BackupFormats.Version2,
                backupId,
                BuildInfo.Version,
                health.SchemaVersion,
                NormalizeReason(reason),
                nowUtc,
                localNow.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                nowUtc,
                health.LastChangeId,
                counts,
                entries);

            TryDelete(partialPath);
            using (var archive = ZipFile.Open(partialPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file.DiskPath, file.ArchivePath, CompressionLevel.Optimal);
                }

                var manifestEntry = archive.CreateEntry(BackupFormats.ManifestPath, CompressionLevel.Optimal);
                await using var output = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, token);
            }

            var verification = await VerifyArchiveAsync(partialPath, token);
            if (!verification.IsValid)
            {
                throw new InvalidDataException($"The completed backup failed verification: {string.Join(" ", verification.Errors)}");
            }

            File.Move(partialPath, finalPath, false);
            RecordSessionVerification(backupId, true, verification.VerifiedAtUtc);

            var settings = await _settingsStore.LoadAsync(token);
            var mirrored = false;
            string? mirrorError = null;
            if (!string.IsNullOrWhiteSpace(settings.MirrorDirectory))
            {
                try
                {
                    await CopyToMirrorAsync(finalPath, settings.MirrorDirectory, localNow, token);
                    mirrored = true;
                    await _stateStore.SetMirrorBackupErrorAsync(null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                    mirrorError = ex.Message;
                    await _stateStore.SetMirrorBackupErrorAsync(mirrorError);
                    _logger.LogWarning(ex, "Local backup succeeded, but mirror copy failed");
                }
            }

            await _stateStore.RecordBackupSuccessAsync(localNow, null, health.LastChangeId);
            if (health.SchemaVersion >= health.SupportedSchemaVersion)
            {
                // Permanent deletion is only allowed after the just-created archive
                // (including the attachment quarantine) passed full verification.
                try
                {
                    await _tasks.PurgeExpiredDeletedTasksAsync(DateTimeOffset.UtcNow, verification.VerifiedAtUtc, token);
                    await _attachmentMaintenance.PurgeExpiredQuarantinedAttachmentsAsync(DateTimeOffset.UtcNow, verification.VerifiedAtUtc, token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Post-backup soft-delete retention cleanup was skipped");
                }
            }
            _logger.LogInformation("Verified backup {BackupId} created at {BackupPath}", backupId, finalPath);
            // Do not prune the second location when today's copy to it failed.
            await RunRetentionAsync(mirrored, token);
            return new BackupCreationResult(true, backupId, finalPath, mirrored, null, mirrorError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup creation failed");
            TryDelete(partialPath);
            await _stateStore.RecordBackupFailureAsync(ex.Message);
            return BackupCreationResult.Failed(ex.Message);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private async Task CopyBackupResourcesAsync(string packageDirectory, CancellationToken token)
    {
        if (Directory.Exists(_paths.AttachmentsDirectory))
        {
            foreach (var source in Directory.EnumerateFiles(_paths.AttachmentsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(source);
                if (name is "." or "..") continue;
                var target = Path.Combine(packageDirectory, "blobs", "attachments", name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(source, target, token);
            }
        }

        if (Directory.Exists(_paths.StatusUpdatesDirectory))
        {
            foreach (var source in Directory.EnumerateFiles(_paths.StatusUpdatesDirectory, "*.json.gz", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(_paths.StatusUpdatesDirectory, source);
                EnsureSafeRelativePath(relative);
                var target = Path.Combine(packageDirectory, "data", "status-updates", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(source, target, token);
            }
        }


        if (Directory.Exists(_paths.AttachmentTrashDirectory))
        {
            foreach (var source in Directory.EnumerateFiles(_paths.AttachmentTrashDirectory, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(_paths.AttachmentTrashDirectory, source);
                EnsureSafeRelativePath(relative);
                var target = Path.Combine(packageDirectory, "blobs", "attachment-trash", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(source, target, token);
            }
        }
    }

    private async Task CopyToMirrorAsync(
        string localPath,
        string mirrorRoot,
        DateTime localNow,
        CancellationToken token)
    {
        var fullMirror = Path.GetFullPath(mirrorRoot);
        if (IsWithin(fullMirror, _paths.AppRoot))
        {
            throw new InvalidDataException("The mirror backup location must be outside the Glance application folder.");
        }
        var directory = Path.Combine(fullMirror, localNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, Path.GetFileName(localPath));
        var partialPath = finalPath + ".partial";
        TryDelete(partialPath);
        await CopyFileAsync(localPath, partialPath, token);
        var verification = await VerifyArchiveAsync(partialPath, token);
        if (!verification.IsValid)
        {
            TryDelete(partialPath);
            throw new InvalidDataException($"The copied backup failed verification: {string.Join(" ", verification.Errors)}");
        }
        File.Move(partialPath, finalPath, true);
    }

    private async Task<IReadOnlyList<ArchiveDescriptor>> FindArchivesAsync(CancellationToken token)
    {
        var result = new List<ArchiveDescriptor>();
        await AddArchivesAsync(_paths.BackupsDirectory, true, result, token);
        var settings = await _settingsStore.LoadAsync(token);
        if (!string.IsNullOrWhiteSpace(settings.MirrorDirectory)
            && Directory.Exists(settings.MirrorDirectory)
            && !PathsEqual(settings.MirrorDirectory, _paths.BackupsDirectory))
        {
            await AddArchivesAsync(settings.MirrorDirectory, false, result, token);
        }
        return result;
    }

    private async Task AddArchivesAsync(
        string root,
        bool isLocal,
        List<ArchiveDescriptor> result,
        CancellationToken token)
    {
        if (!Directory.Exists(root)) return;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to enumerate backup location {BackupRoot}", root);
            return;
        }

        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var manifest = await TryReadManifestAsync(path, token);
                var info = new FileInfo(path);
                if (manifest is not null)
                {
                    result.Add(new ArchiveDescriptor(
                        path,
                        manifest.BackupId,
                        manifest.Format,
                        manifest.CreatedAtUtc,
                        manifest.CreatedAtLocal,
                        manifest.Reason,
                        info.Length,
                        isLocal,
                        manifest));
                }
                else
                {
                    var createdAt = InferLegacyTimestamp(path, info.LastWriteTimeUtc);
                    result.Add(new ArchiveDescriptor(
                        path,
                        CreateLegacyId(path),
                        "legacy",
                        createdAt,
                        createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                        "legacy",
                        info.Length,
                        isLocal,
                        null));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Ignoring unreadable backup archive {BackupPath}", path);
            }
        }
    }

    private async Task<IReadOnlyList<ArchiveDescriptor>> ResolveArchivesAsync(string backupId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128)
        {
            return Array.Empty<ArchiveDescriptor>();
        }
        return (await FindArchivesAsync(token))
            .Where(item => string.Equals(item.BackupId, backupId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.IsLocal)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DescribeCopy(IReadOnlyList<ArchiveDescriptor> descriptors, int index)
    {
        var descriptor = descriptors[index];
        var sameKindCount = descriptors.Count(item => item.IsLocal == descriptor.IsLocal);
        var baseLabel = descriptor.IsLocal ? "Local copy" : "Additional-location copy";
        if (sameKindCount <= 1) return baseLabel;
        var ordinal = descriptors.Take(index + 1).Count(item => item.IsLocal == descriptor.IsLocal);
        return $"{baseLabel} {ordinal}";
    }

    private void RecordSessionVerification(string backupId, bool isValid, DateTimeOffset checkedAtUtc)
    {
        _sessionVerifications[backupId] = new SessionVerificationOutcome(isValid, checkedAtUtc);
    }

    private async Task<BackupVerificationResult> VerifyArchiveAsync(string path, CancellationToken token)
    {
        var errors = new List<string>();
        BackupManifest? manifest = null;
        var verifiedAt = DateTimeOffset.UtcNow;
        var format = "legacy";
        var backupId = CreateLegacyId(path);
        DateTimeOffset? createdAt = null;
        int? schemaVersion = null;
        var verifyDirectory = Path.Combine(_paths.BackupWorkingDirectory, $"verify-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(verifyDirectory);
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 100_000)
            {
                throw new InvalidDataException("The archive contains too many entries.");
            }

            var entries = BuildSafeEntryMap(archive);
            if (!entries.TryGetValue("data/glance.db", out var databaseEntry))
            {
                errors.Add("The archive is missing data/glance.db.");
            }

            if (entries.TryGetValue(BackupFormats.ManifestPath, out var manifestEntry))
            {
                if (manifestEntry.Length > 4 * 1024 * 1024)
                {
                    errors.Add("The backup manifest is unexpectedly large.");
                }
                else
                {
                    await using var input = manifestEntry.Open();
                    manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(input, JsonOptions, token);
                    if (manifest is null)
                    {
                        errors.Add("The backup manifest is empty.");
                    }
                    else
                    {
                        format = manifest.Format;
                        backupId = manifest.BackupId;
                        createdAt = manifest.CreatedAtUtc;
                        schemaVersion = manifest.SchemaVersion;
                        ValidateManifest(manifest, entries, errors);
                        await ValidateManifestHashesAsync(manifest, entries, errors, token);
                    }
                }
            }

            if (manifest is null)
            {
                ValidateLegacyEntries(entries, errors);
            }

            await ValidateStatusPackagesAsync(entries, errors, token);

            if (databaseEntry is not null)
            {
                var databasePath = Path.Combine(verifyDirectory, "glance.db");
                await ExtractEntryAsync(databaseEntry, databasePath, token);
                var health = await _health.CheckDatabaseAsync(databasePath, token);
                schemaVersion ??= health.SchemaVersion;
                if (!health.IsHealthy)
                {
                    errors.AddRange(health.Errors);
                }
                await ValidateResourceReferencesAsync(databasePath, entries.Keys, errors, token);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex.Message);
        }
        finally
        {
            TryDeleteDirectory(verifyDirectory);
        }

        return new BackupVerificationResult(
            errors.Count == 0,
            backupId,
            format,
            createdAt,
            schemaVersion,
            verifiedAt,
            errors.Distinct(StringComparer.Ordinal).ToList());
    }

    private static Dictionary<string, ZipArchiveEntry> BuildSafeEntryMap(ZipArchive archive)
    {
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var path = NormalizeAndValidateArchivePath(entry.FullName);
            if (!result.TryAdd(path, entry))
            {
                throw new InvalidDataException($"The archive contains duplicate path '{path}'.");
            }
        }
        return result;
    }

    private static void ValidateManifest(
        BackupManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> archiveEntries,
        List<string> errors)
    {
        if (!string.Equals(manifest.Format, BackupFormats.Version2, StringComparison.Ordinal))
        {
            errors.Add($"Unsupported backup format '{manifest.Format}'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.BackupId) || manifest.BackupId.Length > 128)
        {
            errors.Add("The backup manifest has an invalid backup ID.");
        }

        var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Entries)
        {
            string safePath;
            try
            {
                safePath = NormalizeAndValidateArchivePath(item.Path);
            }
            catch (InvalidDataException ex)
            {
                errors.Add(ex.Message);
                continue;
            }
            if (!manifestPaths.Add(safePath))
            {
                errors.Add($"The manifest lists '{safePath}' more than once.");
            }
            if (!IsAllowedBackupEntry(safePath))
            {
                errors.Add($"The manifest contains unexpected path '{safePath}'.");
            }
            if (!archiveEntries.ContainsKey(safePath))
            {
                errors.Add($"Manifest entry '{safePath}' is missing from the archive.");
            }
        }

        foreach (var path in archiveEntries.Keys)
        {
            if (string.Equals(path, BackupFormats.ManifestPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!manifestPaths.Contains(path))
            {
                errors.Add($"Archive entry '{path}' is not listed in the manifest.");
            }
        }
    }

    private static async Task ValidateManifestHashesAsync(
        BackupManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> archiveEntries,
        List<string> errors,
        CancellationToken token)
    {
        foreach (var item in manifest.Entries)
        {
            var path = item.Path.Replace('\\', '/');
            if (!archiveEntries.TryGetValue(path, out var entry)) continue;
            if (entry.Length != item.Size)
            {
                errors.Add($"Size mismatch for '{path}'.");
                continue;
            }
            await using var input = entry.Open();
            var hash = await ComputeSha256Async(input, token);
            if (!string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"SHA-256 mismatch for '{path}'.");
            }
        }
    }

    private static void ValidateLegacyEntries(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<string> errors)
    {
        foreach (var path in entries.Keys)
        {
            if (!IsAllowedBackupEntry(path))
            {
                errors.Add($"Legacy archive contains unexpected path '{path}'.");
            }
        }
    }

    private static async Task ValidateResourceReferencesAsync(
        string databasePath,
        IEnumerable<string> archivePaths,
        List<string> errors,
        CancellationToken token)
    {
        var paths = new HashSet<string>(archivePaths, StringComparer.OrdinalIgnoreCase);
        var connectionString = $"Data Source={databasePath};Mode=ReadOnly;Cache=Private;Pooling=False;Foreign Keys=True";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);

        if (await DatabaseHealthService.TableExistsAsync(connection, "tasks", token))
        {
            var columns = await GetColumnsAsync(connection, "tasks", token);
            var selected = new List<string>();
            if (columns.Contains("title_json")) selected.Add("title_json");
            if (columns.Contains("content_json")) selected.Add("content_json");
            if (selected.Count > 0)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {string.Join(", ", selected)} FROM tasks;";
                await using var reader = await command.ExecuteReaderAsync(token);
                var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadAsync(token))
                {
                    for (var index = 0; index < selected.Count; index += 1)
                    {
                        if (reader.IsDBNull(index)) continue;
                        using var document = JsonDocument.Parse(reader.GetString(index));
                        CollectAttachmentReferences(document.RootElement, refs);
                    }
                }
                foreach (var attachment in refs)
                {
                    if (!paths.Contains($"blobs/attachments/{attachment}"))
                    {
                        errors.Add($"Referenced attachment '{attachment}' is missing from the backup.");
                    }
                }
            }
        }

        if (await DatabaseHealthService.TableExistsAsync(connection, "status_update_runs", token))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT relative_json_path FROM status_update_runs;";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var path = reader.GetString(0).Replace('\\', '/').TrimStart('/');
                if (!paths.Contains(path))
                {
                    errors.Add($"Status update package '{path}' is missing from the backup.");
                }
            }
        }
    }

    private async Task ValidateStatusPackagesAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<string> errors,
        CancellationToken token)
    {
        var packages = entries
            .Where(item => item.Key.StartsWith("data/status-updates/", StringComparison.OrdinalIgnoreCase)
                && item.Key.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (packages.Count == 0) return;

        var schema = await GetStatusSchemaAsync(token);
        foreach (var (path, entry) in packages)
        {
            try
            {
                await using var compressed = entry.Open();
                await using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: false);
                using var json = new MemoryStream();
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await gzip.ReadAsync(buffer, token);
                    if (read == 0) break;
                    if (json.Length + read > 64L * 1024 * 1024)
                    {
                        throw new InvalidDataException("The decompressed status JSON exceeds the 64 MB safety limit.");
                    }
                    await json.WriteAsync(buffer.AsMemory(0, read), token);
                }
                json.Position = 0;
                using var document = await JsonDocument.ParseAsync(json, cancellationToken: token);
                var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Flag });
                if (!evaluation.IsValid)
                {
                    errors.Add($"Status package '{path}' does not match status-summary.schema.json.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
            {
                errors.Add($"Status package '{path}' is unreadable: {ex.Message}");
            }
        }
    }

    private async Task<JsonSchema> GetStatusSchemaAsync(CancellationToken token)
    {
        if (_statusSchema is not null) return _statusSchema;
        await _statusSchemaGate.WaitAsync(token);
        try
        {
            if (_statusSchema is not null) return _statusSchema;
            var path = Path.Combine(_paths.AppRoot, "schema", "status-summary.schema.json");
            var schemaText = await File.ReadAllTextAsync(path, token);
            var schemaNode = JsonNode.Parse(schemaText)?.AsObject()
                ?? throw new InvalidDataException("status-summary.schema.json is invalid.");
            schemaNode.Remove("$id");
            _statusSchema = JsonSchema.FromText(schemaNode.ToJsonString());
            return _statusSchema;
        }
        finally
        {
            _statusSchemaGate.Release();
        }
    }

    private static void CollectAttachmentReferences(JsonElement element, HashSet<string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "src", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var src = property.Value.GetString() ?? string.Empty;
                        var marker = src.IndexOf("/attachments/", StringComparison.OrdinalIgnoreCase);
                        if (marker >= 0)
                        {
                            var tail = src[(marker + "/attachments/".Length)..].Split('?', '#')[0];
                            var name = Path.GetFileName(Uri.UnescapeDataString(tail));
                            if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                        }
                    }
                    CollectAttachmentReferences(property.Value, result);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray()) CollectAttachmentReferences(child, result);
                break;
        }
    }

    private async Task<IReadOnlyList<BackupManifestEntry>> ExtractRestorableEntriesAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken token)
    {
        var result = new List<BackupManifestEntry>();
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = BuildSafeEntryMap(archive);
        foreach (var (path, entry) in entries)
        {
            if (!IsAllowedBackupEntry(path)) continue;
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(destination, stagingDirectory))
            {
                throw new InvalidDataException("The backup contains an unsafe restore path.");
            }
            await ExtractEntryAsync(entry, destination, token);
            result.Add(new BackupManifestEntry(path, entry.Length, await ComputeSha256Async(destination, token)));
        }
        if (!result.Any(item => string.Equals(item.Path, "data/glance.db", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The backup has no database to restore.");
        }
        return result;
    }

    private async Task BackupDatabaseAsync(string backupPath, CancellationToken token)
    {
        TryDelete(backupPath);
        var destinationConnectionString = $"Data Source={backupPath};Mode=ReadWriteCreate;Cache=Private;Pooling=False;Foreign Keys=True";
        await using var source = new SqliteConnection(_paths.ConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(token);
        await destination.OpenAsync(token);
        source.BackupDatabase(destination);
        await using (var checkpoint = destination.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
            await checkpoint.ExecuteNonQueryAsync(token);
        }
        await destination.CloseAsync();
        await source.CloseAsync();
        TryDelete(backupPath + "-wal");
        TryDelete(backupPath + "-shm");
    }

    private static async Task<BackupContentCounts> ReadCountsAsync(
        string databasePath,
        IReadOnlyList<BackupManifestEntry> entries,
        CancellationToken token)
    {
        var connectionString = $"Data Source={databasePath};Mode=ReadOnly;Cache=Private;Pooling=False;Foreign Keys=True";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        var tasks = await ReadCountIfTableExistsAsync(connection, "tasks", token);
        var people = await ReadCountIfTableExistsAsync(connection, "people", token);
        var status = await ReadCountIfTableExistsAsync(connection, "status_update_runs", token);
        var attachments = entries.LongCount(entry => entry.Path.StartsWith("blobs/attachments/", StringComparison.OrdinalIgnoreCase));
        return new BackupContentCounts(tasks, people, attachments, status);
    }

    private static async Task<long> ReadCountIfTableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken token)
    {
        if (!await DatabaseHealthService.TableExistsAsync(connection, table, token)) return 0;
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(1));
        return result;
    }

    private static async Task<BackupManifest?> TryReadManifestAsync(string path, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(path);
        var matches = archive.Entries
            .Where(entry => string.Equals(entry.FullName.Replace('\\', '/'), BackupFormats.ManifestPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return null;
        if (matches.Count != 1) throw new InvalidDataException("The archive contains duplicate manifests.");
        if (matches[0].Length > 4 * 1024 * 1024) throw new InvalidDataException("The backup manifest is too large.");
        await using var input = matches[0].Open();
        return await JsonSerializer.DeserializeAsync<BackupManifest>(input, JsonOptions, token)
            ?? throw new InvalidDataException("The backup manifest is empty.");
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = entry.Open();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ComputeSha256Async(input, token);
    }

    private static async Task<string> ComputeSha256Async(Stream input, CancellationToken token)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(input, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(output, value, JsonOptions, token);
            await output.FlushAsync(token);
        }
        File.Move(tempPath, path, true);
    }

    private static bool IsAllowedBackupEntry(string path) =>
        string.Equals(path, "data/glance.db", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("blobs/attachments/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("blobs/attachment-trash/", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith("data/status-updates/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAndValidateArchivePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith('/')
            || normalized.Contains(':')
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"The archive contains unsafe path '{path}'.");
        }
        return normalized;
    }

    private static string NormalizeRelativePath(string path)
    {
        EnsureSafeRelativePath(path);
        return path.Replace('\\', '/');
    }

    private static void EnsureSafeRelativePath(string path)
    {
        if (Path.IsPathFullyQualified(path)
            || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe relative path '{path}'.");
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (PathsEqual(fullPath, fullRoot)) return true;
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReason(string reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim().ToLowerInvariant();
        return value.Length <= 64 ? value : value[..64];
    }

    private static DateTimeOffset InferLegacyTimestamp(string path, DateTime fallbackUtc)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        for (var index = 0; index + 17 <= name.Length; index += 1)
        {
            var candidate = name.Substring(index, 17);
            if (DateTime.TryParseExact(candidate, "yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return new DateTimeOffset(parsed).ToUniversalTime();
            }
        }
        return new DateTimeOffset(DateTime.SpecifyKind(fallbackUtc, DateTimeKind.Utc));
    }

    private static string CreateLegacyId(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return "legacy-" + Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }

    private static void ApplyRetention(string root, DataSafetySettings settings)
    {
        if (!Directory.Exists(root)) return;
        var now = DateTimeOffset.UtcNow;
        var backups = new List<(string Path, BackupManifest Manifest)>();
        foreach (var path in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.Entries.FirstOrDefault(item =>
                    string.Equals(item.FullName.Replace('\\', '/'), BackupFormats.ManifestPath, StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue; // Legacy backups are never pruned automatically.
                using var input = entry.Open();
                var manifest = JsonSerializer.Deserialize<BackupManifest>(input, JsonOptions);
                if (manifest is not null && manifest.Reason.StartsWith("automatic-", StringComparison.OrdinalIgnoreCase))
                {
                    backups.Add((path, manifest));
                }
            }
            catch
            {
                // Never delete an archive that cannot be understood and verified.
            }
        }

        var ordered = backups.OrderByDescending(item => item.Manifest.CreatedAtUtc).ToList();
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ordered.Take(settings.HourlyRetentionCount)) keep.Add(item.Path);

        foreach (var group in ordered
                     .Where(item => now - item.Manifest.CreatedAtUtc <= TimeSpan.FromDays(settings.DailyRetentionDays))
                     .GroupBy(item => item.Manifest.CreatedAtUtc.ToLocalTime().Date))
        {
            keep.Add(group.OrderByDescending(item => item.Manifest.CreatedAtUtc).First().Path);
        }

        var monthlyCutoff = now.AddMonths(-settings.MonthlyRetentionMonths);
        foreach (var group in ordered
                     .Where(item => item.Manifest.CreatedAtUtc >= monthlyCutoff)
                     .GroupBy(item => item.Manifest.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture)))
        {
            keep.Add(group.OrderByDescending(item => item.Manifest.CreatedAtUtc).First().Path);
        }

        foreach (var item in ordered.Where(item => !keep.Contains(item.Path)))
        {
            TryDelete(item.Path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup failures are retried by later maintenance.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Cleanup failures are non-fatal; no live data is stored here.
        }
    }

    private sealed record PackageFile(string DiskPath, string ArchivePath);

    private sealed record SessionVerificationOutcome(bool IsValid, DateTimeOffset CheckedAtUtc);

    private sealed record ArchiveDescriptor(
        string Path,
        string BackupId,
        string Format,
        DateTimeOffset CreatedAtUtc,
        string CreatedAtLocal,
        string Reason,
        long Size,
        bool IsLocal,
        BackupManifest? Manifest);
}
