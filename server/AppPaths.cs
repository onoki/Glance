using System.IO;

namespace Glance.Server;

public sealed class AppPaths
{
    public AppPaths(string appRoot)
    {
        AppRoot = appRoot;
        DataDirectory = Path.Combine(appRoot, "data");
        BlobsDirectory = Path.Combine(appRoot, "blobs");
        AttachmentsDirectory = Path.Combine(BlobsDirectory, "attachments");
        AttachmentTrashDirectory = Path.Combine(BlobsDirectory, "attachment-trash");
        BackupsDirectory = Path.Combine(appRoot, "backups");
        BackupWorkingDirectory = Path.Combine(DataDirectory, "backup-work");
        RecoveryDirectory = Path.Combine(appRoot, "recovery");
        RestoreStagingDirectory = Path.Combine(DataDirectory, "restore-staging");
        StatusUpdatesDirectory = Path.Combine(DataDirectory, "status-updates");
        DatabasePath = Path.Combine(DataDirectory, "glance.db");
        DataSafetySettingsPath = Path.Combine(DataDirectory, "data-safety.json");
        PendingRestorePath = Path.Combine(DataDirectory, "pending-restore.json");
        DocsDirectory = Path.Combine(appRoot, "docs");
        MigrationsDirectory = Path.Combine(appRoot, "schema", "migrations");
        SchemaPath = Path.Combine(appRoot, "schema", "schema.sql");
    }

    public string AppRoot { get; }
    public string DataDirectory { get; }
    public string BlobsDirectory { get; }
    public string AttachmentsDirectory { get; }
    public string AttachmentTrashDirectory { get; }
    public string BackupsDirectory { get; }
    public string BackupWorkingDirectory { get; }
    public string RecoveryDirectory { get; }
    public string RestoreStagingDirectory { get; }
    public string StatusUpdatesDirectory { get; }
    public string DatabasePath { get; }
    public string DataSafetySettingsPath { get; }
    public string PendingRestorePath { get; }
    public string DocsDirectory { get; }
    public string MigrationsDirectory { get; }
    public string SchemaPath { get; }

    public string ConnectionString => $"Data Source={DatabasePath};Cache=Shared;Foreign Keys=True;Default Timeout=5";
    public string ReadOnlyConnectionString => $"Data Source={DatabasePath};Mode=ReadOnly;Cache=Private;Pooling=False;Foreign Keys=True;Default Timeout=5";
}
