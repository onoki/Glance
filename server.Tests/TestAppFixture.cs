using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Server.Tests;

internal sealed class TestAppFixture : IAsyncDisposable
{
    private readonly string _root;

    public AppPaths Paths { get; }
    public TaskRepository Tasks { get; }
    public ChangeLogRepository Changes { get; }
    public AppMetaRepository Meta { get; }

    private TestAppFixture(string root, AppPaths paths)
    {
        _root = root;
        Paths = paths;
        Tasks = new TaskRepository(paths);
        Changes = new ChangeLogRepository(paths);
        Meta = new AppMetaRepository(paths);
    }

    public static TestAppFixture Create()
    {
        var repoRoot = FindRepoRoot();
        var root = Path.Combine(Path.GetTempPath(), "GlanceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        CopyDocs(repoRoot, root);

        var paths = new AppPaths(root);
        var initializer = new DatabaseInitializer(paths, NullLogger<DatabaseInitializer>.Instance);
        initializer.Initialize();

        return new TestAppFixture(root, paths);
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            for (var attempt = 0; attempt < 5; attempt += 1)
            {
                try
                {
                    Directory.Delete(_root, true);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    public static JsonElement CreateTitle(string text)
    {
        var json = $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{Escape(text)}\"}}]}}]}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement CreateContent(string text)
    {
        var json = $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"bulletList\",\"content\":[{{\"type\":\"listItem\",\"content\":[{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{Escape(text)}\"}}]}}]}}]}}]}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var docsPath = Path.Combine(current.FullName, "docs", "schema.sql");
            if (File.Exists(docsPath))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Unable to locate repo root with docs/schema.sql");
    }

    private static void CopyDocs(string repoRoot, string targetRoot)
    {
        var docsSource = Path.Combine(repoRoot, "docs");
        var docsTarget = Path.Combine(targetRoot, "docs");
        Directory.CreateDirectory(docsTarget);
        File.Copy(Path.Combine(docsSource, "schema.sql"), Path.Combine(docsTarget, "schema.sql"), true);

        var migrationsSource = Path.Combine(docsSource, "migrations");
        var migrationsTarget = Path.Combine(docsTarget, "migrations");
        Directory.CreateDirectory(migrationsTarget);
        foreach (var file in Directory.GetFiles(migrationsSource, "*.sql"))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(migrationsTarget, fileName), true);
        }
    }
}
