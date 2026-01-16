using System.IO;

namespace Glance.Server;

public sealed class AppPaths
{
    public AppPaths(string appRoot)
    {
        AppRoot = appRoot;
        DataDirectory = Path.Combine(appRoot, "data");
        BlobsDirectory = Path.Combine(appRoot, "blobs");
        DatabasePath = Path.Combine(DataDirectory, "glance.db");
        DocsDirectory = Path.Combine(appRoot, "docs");
        MigrationsDirectory = Path.Combine(DocsDirectory, "migrations");
        SchemaPath = Path.Combine(DocsDirectory, "schema.sql");
    }

    public string AppRoot { get; }
    public string DataDirectory { get; }
    public string BlobsDirectory { get; }
    public string DatabasePath { get; }
    public string DocsDirectory { get; }
    public string MigrationsDirectory { get; }
    public string SchemaPath { get; }

    public string ConnectionString => $"Data Source={DatabasePath};Cache=Shared";
}
