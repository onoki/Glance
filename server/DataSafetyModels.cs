using System.Text.Json.Serialization;

namespace Glance.Server;

internal static class BackupFormats
{
    public const string Version2 = "glance-backup-v2";
    public const string ManifestPath = "backup-manifest.json";
}

internal sealed record BackupManifest(
    string Format,
    string BackupId,
    string AppVersion,
    int SchemaVersion,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    string CreatedAtLocal,
    DateTimeOffset VerifiedAtUtc,
    long LastChangeId,
    BackupContentCounts Counts,
    IReadOnlyList<BackupManifestEntry> Entries);

internal sealed record BackupManifestEntry(string Path, long Size, string Sha256);

internal sealed record BackupContentCounts(
    long Tasks,
    long People,
    long Attachments,
    long StatusUpdates);

public sealed record BackupCatalogItem(
    string BackupId,
    DateTimeOffset CreatedAtUtc,
    string CreatedAtLocal,
    string Reason,
    string Format,
    int? SchemaVersion,
    long Size,
    BackupCatalogCounts Counts,
    string VerificationStatus,
    DateTimeOffset? VerifiedAtUtc,
    bool HasLocalCopy,
    bool HasMirrorCopy);

public sealed record BackupCatalogCounts(
    long? Tasks,
    long? People,
    long? Attachments,
    long? StatusUpdates);

public sealed record BackupVerificationResult(
    bool IsValid,
    string BackupId,
    string Format,
    DateTimeOffset? CreatedAtUtc,
    int? SchemaVersion,
    DateTimeOffset VerifiedAtUtc,
    IReadOnlyList<string> Errors);

internal sealed record BackupCreationResult(
    bool Success,
    string? BackupId,
    string? LocalPath,
    bool Mirrored,
    string? Error,
    string? MirrorError)
{
    public static BackupCreationResult Failed(string error) =>
        new(false, null, null, false, error, null);
}

public sealed class DataSafetySettings
{
    public bool HourlyBackupsEnabled { get; set; } = true;
    public string? MirrorDirectory { get; set; }
    public int HourlyRetentionCount { get; set; } = 48;
    public int DailyRetentionDays { get; set; } = 30;
    public int MonthlyRetentionMonths { get; set; } = 12;
}

public sealed record DataSafetyLocationTestRequest(string? Location);

public sealed record DataSafetyLocationTestResult(bool Available, bool Writable, string Message);

public sealed record BackupRestoreRequest(string BackupId);

public sealed record BackupRestorePlanResponse(
    bool Ready,
    string BackupId,
    string Message,
    bool RestartRequired,
    string? EmergencyBackupId);

internal sealed class PendingRestorePlan
{
    public string Format { get; set; } = "glance-restore-v1";
    public string RestoreId { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string StagingDirectory { get; set; } = string.Empty;
    public string RescueDirectory { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<BackupManifestEntry> Entries { get; set; } = new();
}

public sealed record StartupSafetyInfo(
    bool Healthy,
    bool RecoveryMode,
    string? Message,
    int? SchemaVersion,
    int SupportedSchemaVersion,
    bool RestoreApplied)
{
    public bool PendingRestore { get; init; }
}

internal sealed class DataSafetyStartupState
{
    private readonly object _sync = new();
    private StartupSafetyInfo _info = new(true, false, null, null, 0, false);

    public StartupSafetyInfo Info
    {
        get
        {
            lock (_sync)
            {
                return _info;
            }
        }
    }

    public void Set(StartupSafetyInfo info)
    {
        lock (_sync)
        {
            _info = info;
        }
    }
}

internal sealed record DatabaseHealthResult(
    bool IsHealthy,
    int SchemaVersion,
    int SupportedSchemaVersion,
    long LastChangeId,
    IReadOnlyList<string> Errors);
