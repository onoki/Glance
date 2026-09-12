using System.Text.Json;
using Glance.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Glance.Server.Tests;

public sealed class SoftDeleteTests
{
    [Fact]
    public void Migration005_UpgradesExistingVersionFourDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "GlanceSoftDeleteMigrationTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.MigrationsDirectory);
        var migrationSource = Path.Combine(FindRepoRoot(), "schema", "migrations", "005_soft_delete.sql");
        File.Copy(migrationSource, Path.Combine(paths.MigrationsDirectory, "005_soft_delete.sql"));

        try
        {
            using (var connection = new SqliteConnection(paths.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE tasks (id TEXT PRIMARY KEY);
                    CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);
                    INSERT INTO schema_migrations(version, applied_at) VALUES(4, 0);
                    """;
                command.ExecuteNonQuery();
            }

            new DatabaseInitializer(paths, NullLogger<DatabaseInitializer>.Instance).Initialize();

            using var verify = new SqliteConnection(paths.ConnectionString);
            verify.Open();
            using var columns = verify.CreateCommand();
            columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('tasks') WHERE name = 'deleted_at';";
            Assert.Equal(1L, columns.ExecuteScalar());
            using var version = verify.CreateCommand();
            version.CommandText = "SELECT MAX(version) FROM schema_migrations;";
            Assert.Equal(5L, version.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Delete_HidesTaskButKeepsRecoverableRow_AndRestoreUsesSameId()
    {
        await using var app = TestAppFixture.Create();
        var person = await app.People.CreatePersonAsync("Alice", CancellationToken.None);
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.PeopleMain,
            TestAppFixture.CreateTitle("Recover this"),
            TestAppFixture.CreateContent("Important note"),
            1,
            null,
            null,
            person.Id), CancellationToken.None);

        Assert.True(await app.Tasks.DeleteTaskAsync(created.TaskId, CancellationToken.None));
        Assert.Empty(await app.Tasks.GetPersonTasksAsync(person.Id, 0, CancellationToken.None));
        Assert.DoesNotContain(await app.Tasks.SearchAsync("Recover", CancellationToken.None), task => task.Id == created.TaskId);
        Assert.DoesNotContain(await app.Tasks.GetHistoryTasksAsync(CancellationToken.None), task => task.Id == created.TaskId);

        await using (var connection = new SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT deleted_at FROM tasks WHERE id = $id;";
            command.Parameters.AddWithValue("$id", created.TaskId);
            Assert.IsType<long>(await command.ExecuteScalarAsync());
        }

        var restored = await app.Tasks.RestoreTaskAsync(created.TaskId, CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Contains(await app.Tasks.GetPersonTasksAsync(person.Id, 0, CancellationToken.None), task => task.Id == created.TaskId);
        Assert.Contains(await app.Tasks.SearchAsync("Recover", CancellationToken.None), task => task.Id == created.TaskId);
        var changes = await app.Changes.GetChangesAsync(0, CancellationToken.None);
        Assert.Contains(changes.Changes, change => change.EntityId == created.TaskId && change.ChangeType == "restore");
    }

    [Fact]
    public async Task PurgeDeletedTasks_RequiresVerifiedBackupNewEnoughToContainRows()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Old deleted task"),
            TestAppFixture.CreateContent("Retain first"),
            1,
            null,
            null), CancellationToken.None);
        await app.Tasks.DeleteTaskAsync(created.TaskId, CancellationToken.None);

        var deletedAt = await GetDeletedAtAsync(app.Paths.ConnectionString, created.TaskId);
        var afterRetention = DateTimeOffset.FromUnixTimeMilliseconds(deletedAt).AddDays(31);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Tasks.PurgeExpiredDeletedTasksAsync(
            afterRetention,
            null,
            CancellationToken.None));
        Assert.Equal(1, await CountTaskRowsAsync(app.Paths.ConnectionString, created.TaskId));

        var purged = await app.Tasks.PurgeExpiredDeletedTasksAsync(
            afterRetention,
            DateTimeOffset.FromUnixTimeMilliseconds(deletedAt + 1),
            CancellationToken.None);
        Assert.Equal(1, purged);
        Assert.Equal(0, await CountTaskRowsAsync(app.Paths.ConnectionString, created.TaskId));
    }

    [Fact]
    public async Task AttachmentGc_ScansTitlesAndSoftDeletedTasks_AndQuarantinesOrphans()
    {
        await using var app = TestAppFixture.Create();
        Directory.CreateDirectory(app.Paths.AttachmentsDirectory);
        var referencedName = "title-reference.png";
        var orphanName = "orphan.png";
        await File.WriteAllTextAsync(Path.Combine(app.Paths.AttachmentsDirectory, referencedName), "referenced");
        await File.WriteAllTextAsync(Path.Combine(app.Paths.AttachmentsDirectory, orphanName), "orphan");

        using var titleDocument = JsonDocument.Parse(
            $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"image\",\"attrs\":{{\"src\":\"/attachments/{referencedName}\"}}}}]}}]}}");
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            titleDocument.RootElement.Clone(),
            TestAppFixture.CreateContent("Image is in the title"),
            1,
            null,
            null), CancellationToken.None);

        var maintenance = new AttachmentMaintenance(app.Paths, NullLogger<AttachmentMaintenance>.Instance);
        Assert.Equal(1, await maintenance.RunAttachmentGcAsync(CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(app.Paths.AttachmentsDirectory, referencedName)));
        Assert.False(File.Exists(Path.Combine(app.Paths.AttachmentsDirectory, orphanName)));
        Assert.Single(Directory.GetFiles(maintenance.QuarantineDirectory, orphanName, SearchOption.AllDirectories));

        await app.Tasks.DeleteTaskAsync(created.TaskId, CancellationToken.None);
        Assert.Equal(0, await maintenance.RunAttachmentGcAsync(CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(app.Paths.AttachmentsDirectory, referencedName)));
    }

    [Fact]
    public async Task AttachmentGc_InvalidRichTextFailsClosed()
    {
        await using var app = TestAppFixture.Create();
        Directory.CreateDirectory(app.Paths.AttachmentsDirectory);
        var orphanPath = Path.Combine(app.Paths.AttachmentsDirectory, "retain-on-parse-error.png");
        await File.WriteAllTextAsync(orphanPath, "important");
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Will be corrupted for test"),
            TestAppFixture.CreateContent("Valid content"),
            1,
            null,
            null), CancellationToken.None);

        await using (var connection = new SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET title_json = '{invalid' WHERE id = $id;";
            command.Parameters.AddWithValue("$id", created.TaskId);
            await command.ExecuteNonQueryAsync();
        }

        var maintenance = new AttachmentMaintenance(app.Paths, NullLogger<AttachmentMaintenance>.Instance);
        Assert.Equal(0, await maintenance.RunAttachmentGcAsync(CancellationToken.None));
        Assert.True(File.Exists(orphanPath));
        Assert.False(Directory.Exists(maintenance.QuarantineDirectory));
    }

    [Fact]
    public async Task AttachmentQuarantine_PurgesOnlyAfterThirtyDaysAndVerifiedBackup()
    {
        await using var app = TestAppFixture.Create();
        Directory.CreateDirectory(app.Paths.AttachmentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(app.Paths.AttachmentsDirectory, "expired.png"), "old orphan");
        var maintenance = new AttachmentMaintenance(app.Paths, NullLogger<AttachmentMaintenance>.Instance);
        await maintenance.RunAttachmentGcAsync(CancellationToken.None);

        var currentFolder = Assert.Single(Directory.GetDirectories(maintenance.QuarantineDirectory));
        var now = DateTimeOffset.UtcNow;
        var oldTimestamp = now.AddDays(-31).ToUnixTimeMilliseconds().ToString();
        var oldFolder = Path.Combine(maintenance.QuarantineDirectory, oldTimestamp);
        Directory.Move(currentFolder, oldFolder);

        Assert.Equal(0, await maintenance.PurgeExpiredQuarantinedAttachmentsAsync(now, null, CancellationToken.None));
        Assert.Single(Directory.GetFiles(oldFolder));
        Assert.Equal(1, await maintenance.PurgeExpiredQuarantinedAttachmentsAsync(now, now, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(oldFolder));
    }

    private static async Task<long> GetDeletedAtAsync(string connectionString, string taskId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT deleted_at FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountTaskRowsAsync(string connectionString, string taskId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "schema", "migrations", "005_soft_delete.sql")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Unable to locate Glance repository root.");
    }
}
