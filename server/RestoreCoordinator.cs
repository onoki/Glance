using System.Security.Cryptography;
using System.Text.Json;

namespace Glance.Server;

internal sealed record RestoreApplyResult(bool Applied, bool Failed, string? Message);

internal sealed class RestoreCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly DatabaseHealthService _health;
    private readonly ILogger<RestoreCoordinator> _logger;
    private readonly Action<string>? _faultInjector;

    public RestoreCoordinator(
        AppPaths paths,
        DatabaseHealthService health,
        ILogger<RestoreCoordinator> logger)
        : this(paths, health, logger, null)
    {
    }

    internal RestoreCoordinator(
        AppPaths paths,
        DatabaseHealthService health,
        ILogger<RestoreCoordinator> logger,
        Action<string>? faultInjector)
    {
        _paths = paths;
        _health = health;
        _logger = logger;
        _faultInjector = faultInjector;
    }

    public async Task<RestoreApplyResult> ApplyPendingRestoreAsync(CancellationToken token)
    {
        if (!File.Exists(_paths.PendingRestorePath))
        {
            return new RestoreApplyResult(false, false, null);
        }

        PendingRestorePlan? plan = null;
        try
        {
            await using var input = File.OpenRead(_paths.PendingRestorePath);
            plan = await JsonSerializer.DeserializeAsync<PendingRestorePlan>(input, JsonOptions, token);
            if (plan is null || !string.Equals(plan.Format, "glance-restore-v1", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The pending restore plan is invalid.");
            }
            ValidatePlanPaths(plan);
            await VerifyStagedFilesAsync(plan, token);
            var stagedDatabase = GetStagedPath(plan, "data/glance.db");
            var stagedHealth = await _health.CheckDatabaseAsync(stagedDatabase, token);
            if (!stagedHealth.IsHealthy)
            {
                throw new InvalidDataException($"The staged restore database is invalid: {string.Join(" ", stagedHealth.Errors)}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pending restore validation failed; live data was not changed");
            var evidence = QuarantineFailedRestore(plan, "validation", ex);
            return new RestoreApplyResult(
                false,
                true,
                $"Restore validation failed before any live data was changed and the stale plan was cleared: {ex.Message}{FormatEvidenceLocation(evidence)}");
        }

        try
        {
            await ApplyPlanAsync(plan, token);
            _faultInjector?.Invoke("after-install");
            var installedHealth = await _health.CheckLiveDatabaseAsync(token);
            if (!installedHealth.IsHealthy)
            {
                throw new InvalidDataException($"The restored database failed its final check: {string.Join(" ", installedHealth.Errors)}");
            }

            File.Delete(_paths.PendingRestorePath);
            TryDeleteDirectory(plan.StagingDirectory);
            _logger.LogWarning(
                "Applied restore {RestoreId} from backup {BackupId}; previous live state remains at {RescueDirectory}",
                plan.RestoreId,
                plan.BackupId,
                plan.RescueDirectory);
            return new RestoreApplyResult(true, false, "The selected backup was restored successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore installation failed; attempting rollback from rescue copy");
            try
            {
                RollBackFromRescue(plan);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogCritical(rollbackEx, "Restore rollback failed; rescue state remains at {RescueDirectory}", plan.RescueDirectory);
                var evidence = QuarantineFailedRestore(plan, "install-and-rollback", ex, rollbackEx);
                return new RestoreApplyResult(
                    false,
                    true,
                    $"Restore and automatic rollback both failed. Rescue evidence remains at '{plan.RescueDirectory}'. {ex.Message}{FormatEvidenceLocation(evidence)}");
            }
            var rollbackEvidence = QuarantineFailedRestore(plan, "install-rolled-back", ex);
            return new RestoreApplyResult(
                false,
                true,
                $"Restore failed, the previous live state was put back, and the stale plan was cleared: {ex.Message}{FormatEvidenceLocation(rollbackEvidence)}");
        }
    }

    private async Task ApplyPlanAsync(PendingRestorePlan plan, CancellationToken token)
    {
        Directory.CreateDirectory(plan.RescueDirectory);
        var rescueData = Path.Combine(plan.RescueDirectory, "data");
        var rescueAttachments = Path.Combine(plan.RescueDirectory, "blobs", "attachments");
        var rescueAttachmentTrash = Path.Combine(plan.RescueDirectory, "blobs", "attachment-trash");
        var rescueStatus = Path.Combine(rescueData, "status-updates");
        var rescuePreparedMarker = Path.Combine(plan.RescueDirectory, ".original-state-preserved");
        var originalAlreadyPreserved = File.Exists(rescuePreparedMarker);
        Directory.CreateDirectory(rescueData);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        PreserveOriginalFile(_paths.DatabasePath, Path.Combine(rescueData, "glance.db"));
        PreserveOriginalFile(_paths.DatabasePath + "-wal", Path.Combine(rescueData, "glance.db-wal"));
        PreserveOriginalFile(_paths.DatabasePath + "-shm", Path.Combine(rescueData, "glance.db-shm"));

        if (originalAlreadyPreserved || Directory.Exists(rescueAttachments))
        {
            if (Directory.Exists(_paths.AttachmentsDirectory)) Directory.Delete(_paths.AttachmentsDirectory, true);
        }
        else if (Directory.Exists(_paths.AttachmentsDirectory))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rescueAttachments)!);
            Directory.Move(_paths.AttachmentsDirectory, rescueAttachments);
        }

        if (originalAlreadyPreserved || Directory.Exists(rescueAttachmentTrash))
        {
            if (Directory.Exists(_paths.AttachmentTrashDirectory)) Directory.Delete(_paths.AttachmentTrashDirectory, true);
        }
        else if (Directory.Exists(_paths.AttachmentTrashDirectory))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rescueAttachmentTrash)!);
            Directory.Move(_paths.AttachmentTrashDirectory, rescueAttachmentTrash);
        }

        if (originalAlreadyPreserved || Directory.Exists(rescueStatus))
        {
            DeleteStatusPackages(_paths.StatusUpdatesDirectory);
        }
        else
        {
            MoveStatusPackages(_paths.StatusUpdatesDirectory, rescueStatus);
        }

        if (!originalAlreadyPreserved)
        {
            File.WriteAllText(rescuePreparedMarker, plan.BackupId);
        }

        Directory.CreateDirectory(_paths.DataDirectory);
        await CopyFileAsync(GetStagedPath(plan, "data/glance.db"), _paths.DatabasePath, token);

        var stagedAttachments = Path.Combine(plan.StagingDirectory, "blobs", "attachments");
        if (Directory.Exists(stagedAttachments))
        {
            CopyDirectory(stagedAttachments, _paths.AttachmentsDirectory);
        }
        else
        {
            Directory.CreateDirectory(_paths.AttachmentsDirectory);
        }


        var stagedAttachmentTrash = Path.Combine(plan.StagingDirectory, "blobs", "attachment-trash");
        if (Directory.Exists(stagedAttachmentTrash))
        {
            CopyDirectory(stagedAttachmentTrash, _paths.AttachmentTrashDirectory);
        }

        var stagedStatus = Path.Combine(plan.StagingDirectory, "data", "status-updates");
        if (Directory.Exists(stagedStatus))
        {
            foreach (var source in Directory.EnumerateFiles(stagedStatus, "*.json.gz", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagedStatus, source);
                var destination = Path.Combine(_paths.StatusUpdatesDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(source, destination, token);
            }
        }
    }

    private void RollBackFromRescue(PendingRestorePlan plan)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var rescueData = Path.Combine(plan.RescueDirectory, "data");
        var rescueAttachments = Path.Combine(plan.RescueDirectory, "blobs", "attachments");
        var rescueAttachmentTrash = Path.Combine(plan.RescueDirectory, "blobs", "attachment-trash");
        var rescueStatus = Path.Combine(rescueData, "status-updates");

        TryDelete(_paths.DatabasePath);
        TryDelete(_paths.DatabasePath + "-wal");
        TryDelete(_paths.DatabasePath + "-shm");
        MoveIfExists(Path.Combine(rescueData, "glance.db"), _paths.DatabasePath);
        MoveIfExists(Path.Combine(rescueData, "glance.db-wal"), _paths.DatabasePath + "-wal");
        MoveIfExists(Path.Combine(rescueData, "glance.db-shm"), _paths.DatabasePath + "-shm");

        if (Directory.Exists(_paths.AttachmentsDirectory)) Directory.Delete(_paths.AttachmentsDirectory, true);
        if (Directory.Exists(rescueAttachments))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.AttachmentsDirectory)!);
            Directory.Move(rescueAttachments, _paths.AttachmentsDirectory);
        }


        if (Directory.Exists(_paths.AttachmentTrashDirectory)) Directory.Delete(_paths.AttachmentTrashDirectory, true);
        if (Directory.Exists(rescueAttachmentTrash))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.AttachmentTrashDirectory)!);
            Directory.Move(rescueAttachmentTrash, _paths.AttachmentTrashDirectory);
        }

        DeleteStatusPackages(_paths.StatusUpdatesDirectory);
        if (Directory.Exists(rescueStatus))
        {
            MoveStatusPackages(rescueStatus, _paths.StatusUpdatesDirectory);
        }

        // If the process survived long enough to complete rollback, a later
        // startup must never interpret this rescue folder as an in-progress swap.
        TryDeleteNoThrow(Path.Combine(plan.RescueDirectory, ".original-state-preserved"));
    }

    private string? QuarantineFailedRestore(
        PendingRestorePlan? plan,
        string phase,
        Exception failure,
        Exception? rollbackFailure = null)
    {
        var quarantineId = $"failed-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var quarantineDirectory = Path.Combine(_paths.RecoveryDirectory, quarantineId);
        string? evidenceDirectory = null;
        try
        {
            Directory.CreateDirectory(quarantineDirectory);
            evidenceDirectory = quarantineDirectory;
            var details = $"Phase: {phase}{Environment.NewLine}Time (UTC): {DateTimeOffset.UtcNow:O}{Environment.NewLine}Failure: {failure}{Environment.NewLine}";
            if (rollbackFailure is not null)
            {
                details += $"Rollback failure: {rollbackFailure}{Environment.NewLine}";
            }
            File.WriteAllText(Path.Combine(quarantineDirectory, "failure.txt"), details);

            if (plan is not null
                && !string.IsNullOrWhiteSpace(plan.StagingDirectory)
                && Path.IsPathFullyQualified(plan.StagingDirectory)
                && IsWithin(plan.StagingDirectory, _paths.RestoreStagingDirectory)
                && Directory.Exists(plan.StagingDirectory))
            {
                try
                {
                    Directory.Move(plan.StagingDirectory, Path.Combine(quarantineDirectory, "staging"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to move restore staging into {QuarantineDirectory}; it remains at {StagingDirectory}", quarantineDirectory, plan.StagingDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to create the primary failed-restore evidence directory");
            evidenceDirectory = null;
        }

        if (File.Exists(_paths.PendingRestorePath))
        {
            var evidencePath = evidenceDirectory is null
                ? _paths.PendingRestorePath + $".failed-{Guid.NewGuid():N}"
                : Path.Combine(evidenceDirectory, "pending-restore.json");
            try
            {
                File.Move(_paths.PendingRestorePath, evidencePath, false);
            }
            catch (Exception moveEx)
            {
                _logger.LogError(moveEx, "Failed to quarantine pending restore plan; attempting to clear the active plan name");
                try
                {
                    File.Copy(_paths.PendingRestorePath, evidencePath, false);
                }
                catch (Exception copyEx)
                {
                    _logger.LogError(copyEx, "Failed to preserve an additional copy of the pending restore plan");
                }
                TryDeleteNoThrow(_paths.PendingRestorePath);
            }
        }

        return evidenceDirectory;
    }

    private static string FormatEvidenceLocation(string? evidenceDirectory) =>
        evidenceDirectory is null ? string.Empty : $" Failure evidence was retained at '{evidenceDirectory}'.";

    private static void ValidatePlanPaths(PendingRestorePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.RestoreId)
            || string.IsNullOrWhiteSpace(plan.BackupId)
            || plan.Entries.Count == 0)
        {
            throw new InvalidDataException("The pending restore plan is incomplete.");
        }
        if (!IsWithin(plan.StagingDirectory, Path.GetDirectoryName(plan.StagingDirectory)!)
            || !Path.IsPathFullyQualified(plan.StagingDirectory)
            || !Path.IsPathFullyQualified(plan.RescueDirectory))
        {
            throw new InvalidDataException("The pending restore plan uses invalid directories.");
        }
    }

    private async Task VerifyStagedFilesAsync(PendingRestorePlan plan, CancellationToken token)
    {
        if (!IsWithin(plan.StagingDirectory, _paths.RestoreStagingDirectory)
            || !IsWithin(plan.RescueDirectory, _paths.RecoveryDirectory))
        {
            throw new InvalidDataException("The restore plan points outside Glance's staging or recovery folders.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Entries)
        {
            var path = item.Path.Replace('\\', '/');
            if (!seen.Add(path) || !IsAllowedRestoreEntry(path))
            {
                throw new InvalidDataException($"The restore plan contains invalid path '{item.Path}'.");
            }
            var stagedPath = GetStagedPath(plan, path);
            if (!File.Exists(stagedPath) || new FileInfo(stagedPath).Length != item.Size)
            {
                throw new InvalidDataException($"Staged restore file '{path}' is missing or has changed size.");
            }
            await using var input = File.OpenRead(stagedPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
            if (!string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Staged restore file '{path}' has changed.");
            }
        }
        if (!seen.Contains("data/glance.db"))
        {
            throw new InvalidDataException("The restore plan is missing its database.");
        }
    }

    private static string GetStagedPath(PendingRestorePlan plan, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(plan.StagingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(path, plan.StagingDirectory))
        {
            throw new InvalidDataException("The restore plan contains an unsafe path.");
        }
        return path;
    }

    private static bool IsAllowedRestoreEntry(string path) =>
        string.Equals(path, "data/glance.db", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("blobs/attachments/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("blobs/attachment-trash/", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith("data/status-updates/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase));

    private static void MoveStatusPackages(string sourceRoot, string destinationRoot)
    {
        if (!Directory.Exists(sourceRoot)) return;
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.json.gz", SearchOption.AllDirectories).ToList())
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, true);
        }
    }

    private static void DeleteStatusPackages(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.json.gz", SearchOption.AllDirectories).ToList())
        {
            TryDelete(path);
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, true);
    }

    private static void PreserveOriginalFile(string source, string destination)
    {
        if (File.Exists(destination))
        {
            // A prior interrupted attempt already preserved the original. Never
            // overwrite that rescue copy with partially installed state.
            TryDelete(source);
            return;
        }
        MoveIfExists(source, destination);
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(fullRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDeleteNoThrow(string path)
    {
        try
        {
            TryDelete(path);
        }
        catch
        {
            // The active pending plan is quarantined separately. A leftover
            // marker is only evidence and must not turn a successful rollback
            // into another destructive attempt.
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
            // Restore is complete; leftover staging is harmless and can be inspected.
        }
    }
}
