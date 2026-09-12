using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Glance.Server.Tests;

public sealed class DataSafetyTests
{
    [Fact]
    public async Task ServerHost_ExposesDataSafetyCatalogAndStartupState()
    {
        await using var fixture = TestAppFixture.Create();
        var port = ReserveFreePort();
        var app = await ServerHost.StartAsync(fixture.Paths.AppRoot, port, CancellationToken.None);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            using var startup = await client.GetAsync("/api/data-safety/startup");
            startup.EnsureSuccessStatusCode();
            using var startupJson = JsonDocument.Parse(await startup.Content.ReadAsStringAsync());
            Assert.True(startupJson.RootElement.GetProperty("healthy").GetBoolean());
            Assert.False(startupJson.RootElement.GetProperty("pendingRestore").GetBoolean());

            await File.WriteAllTextAsync(fixture.Paths.PendingRestorePath, "{}");
            using var pendingStartup = await client.GetAsync("/api/data-safety/startup");
            pendingStartup.EnsureSuccessStatusCode();
            using var pendingStartupJson = JsonDocument.Parse(await pendingStartup.Content.ReadAsStringAsync());
            Assert.True(pendingStartupJson.RootElement.GetProperty("pendingRestore").GetBoolean());

            using var backups = await client.GetAsync("/api/backups");
            backups.EnsureSuccessStatusCode();
            using var backupJson = JsonDocument.Parse(await backups.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, backupJson.RootElement.GetProperty("backups").ValueKind);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task BackupV2_IsCataloguedAndFullyVerified()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Safety copy"),
            TestAppFixture.CreateContent("Keep this"),
            1,
            null,
            null), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(created.TaskId));

        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);

        Assert.True(backup.Success, backup.Error);
        Assert.NotNull(backup.LocalPath);
        using (var archive = ZipFile.OpenRead(backup.LocalPath!))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == BackupFormats.ManifestPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "data/glance.db");
        }

        var catalog = await service.GetCatalogAsync(CancellationToken.None);
        var item = Assert.Single(catalog, item => item.BackupId == backup.BackupId);
        Assert.Equal(BackupFormats.Version2, item.Format);
        Assert.Equal(1, item.Counts.Tasks);
        Assert.True(item.HasLocalCopy);
        Assert.Equal("verified", item.VerificationStatus);

        var verification = await service.VerifyBackupAsync(backup.BackupId!, CancellationToken.None);
        Assert.True(verification.IsValid, string.Join(" ", verification.Errors));
        Assert.Equal(BackupFormats.Version2, verification.Format);
    }

    [Fact]
    public async Task BackupVerification_RejectsChangedArchiveContent()
    {
        await using var app = TestAppFixture.Create();
        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Original"),
            TestAppFixture.CreateContent("Original"),
            1,
            null,
            null), CancellationToken.None);
        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
        Assert.True(backup.Success, backup.Error);

        await CorruptArchivedDatabaseAsync(backup.LocalPath!);

        var verification = await service.VerifyBackupFileAsync(backup.LocalPath!, CancellationToken.None);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Errors, error =>
            error.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
            || error.Contains("database", StringComparison.OrdinalIgnoreCase));

        var selectedVerification = await service.VerifyBackupAsync(backup.BackupId!, CancellationToken.None);
        Assert.False(selectedVerification.IsValid);
        Assert.Contains(selectedVerification.Errors, error =>
            error.Contains("Local copy", StringComparison.OrdinalIgnoreCase));
        var failedCatalogItem = Assert.Single(
            await service.GetCatalogAsync(CancellationToken.None),
            item => item.BackupId == backup.BackupId);
        Assert.Equal("failed", failedCatalogItem.VerificationStatus);
        Assert.Null(failedCatalogItem.VerifiedAtUtc);
    }

    [Fact]
    public async Task BackupSelection_FallsBackFromCorruptLocalCopyToValidMirror()
    {
        await using var app = TestAppFixture.Create();
        var mirrorRoot = Path.Combine(Path.GetTempPath(), "GlanceMirrorTests", Guid.NewGuid().ToString("N"));
        try
        {
            await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
                TaskPages.DashboardMain,
                TestAppFixture.CreateTitle("Mirrored"),
                TestAppFixture.CreateContent("Fallback"),
                1,
                null,
                null), CancellationToken.None);
            var service = CreateService(app);
            await service.SaveSettingsAsync(new DataSafetySettings { MirrorDirectory = mirrorRoot }, CancellationToken.None);
            var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
            Assert.True(backup.Success, backup.Error);
            Assert.True(backup.Mirrored, backup.MirrorError);
            var mirrorPath = Assert.Single(Directory.EnumerateFiles(mirrorRoot, "*.zip", SearchOption.AllDirectories));
            var validArchive = await File.ReadAllBytesAsync(mirrorPath);

            await CorruptArchivedDatabaseAsync(backup.LocalPath!);
            await CorruptArchivedDatabaseAsync(mirrorPath);

            var failed = await service.VerifyBackupAsync(backup.BackupId!, CancellationToken.None);
            Assert.False(failed.IsValid);
            var failedCatalog = Assert.Single(
                await service.GetCatalogAsync(CancellationToken.None),
                item => item.BackupId == backup.BackupId);
            Assert.Equal("failed", failedCatalog.VerificationStatus);

            await File.WriteAllBytesAsync(mirrorPath, validArchive);

            var verification = await service.VerifyBackupAsync(backup.BackupId!, CancellationToken.None);
            Assert.True(verification.IsValid, string.Join(" ", verification.Errors));
            var recoveredCatalog = Assert.Single(
                await service.GetCatalogAsync(CancellationToken.None),
                item => item.BackupId == backup.BackupId);
            Assert.Equal("verified", recoveredCatalog.VerificationStatus);
            Assert.NotNull(recoveredCatalog.VerifiedAtUtc);

            var restore = await service.StageRestoreAsync(backup.BackupId!, false, CancellationToken.None);
            Assert.True(restore.Ready);
            Assert.True(File.Exists(app.Paths.PendingRestorePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(mirrorRoot)) Directory.Delete(mirrorRoot, true);
        }
    }

    [Fact]
    public async Task ScheduledReverificationFailure_MarksCatalogAsFailed()
    {
        await using var app = TestAppFixture.Create();
        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Scheduled verification"),
            TestAppFixture.CreateContent("Detect later corruption"),
            1,
            null,
            null), CancellationToken.None);
        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
        Assert.True(backup.Success, backup.Error);
        await CorruptArchivedDatabaseAsync(backup.LocalPath!);

        await service.ReverifyNewestIfDueAsync(CancellationToken.None);

        var item = Assert.Single(
            await service.GetCatalogAsync(CancellationToken.None),
            candidate => candidate.BackupId == backup.BackupId);
        Assert.Equal("failed", item.VerificationStatus);
        Assert.Null(item.VerifiedAtUtc);
    }

    [Fact]
    public async Task BackupCreation_RejectsUnreadableReferencedStatusPackage()
    {
        await using var app = TestAppFixture.Create();
        var reportDate = "2026-08-02";
        var relative = $"data/status-updates/{reportDate}/StatusSummary.json.gz";
        var packagePath = Path.Combine(app.Paths.AppRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "this is not gzip data");

        await using (var connection = new SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO status_update_runs(
                  report_date, report_id, input_revision, document_status,
                  created_at, updated_at, completed_at, input_sha256, relative_json_path)
                VALUES ($date, $id, 1, 'input', 1, 1, NULL, 'abc', $path);
                """;
            command.Parameters.AddWithValue("$date", reportDate);
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$path", relative);
            await command.ExecuteNonQueryAsync();
        }

        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);

        Assert.False(backup.Success);
        Assert.Contains("status package", backup.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(app.Paths.BackupsDirectory, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Restore_UsesSelectedSnapshotAndPreservesARescueCopy()
    {
        await using var app = TestAppFixture.Create();
        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Retained"),
            TestAppFixture.CreateContent("First state"),
            1,
            null,
            null), CancellationToken.None);

        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
        Assert.True(backup.Success, backup.Error);

        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Later change"),
            TestAppFixture.CreateContent("Second state"),
            2,
            null,
            null), CancellationToken.None);

        var plan = await service.StageRestoreAsync(backup.BackupId!, true, CancellationToken.None);
        Assert.True(plan.Ready);
        Assert.True(plan.RestartRequired);
        Assert.NotNull(plan.EmergencyBackupId);

        SqliteConnection.ClearAllPools();
        var health = CreateHealth(app.Paths);
        var coordinator = new RestoreCoordinator(
            app.Paths,
            health,
            NullLogger<RestoreCoordinator>.Instance);
        var applied = await coordinator.ApplyPendingRestoreAsync(CancellationToken.None);
        Assert.True(applied.Applied, applied.Message);

        await using var connection = new SqliteConnection(app.Paths.ReadOnlyConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        Assert.True(Directory.EnumerateDirectories(app.Paths.RecoveryDirectory, "pre-restore-*", SearchOption.TopDirectoryOnly).Any());
        Assert.False(File.Exists(app.Paths.PendingRestorePath));
    }

    [Fact]
    public async Task RestoreValidationFailure_QuarantinesPlanAndAllowsRestaging()
    {
        await using var app = TestAppFixture.Create();
        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Restage after failure"),
            TestAppFixture.CreateContent("Original"),
            1,
            null,
            null), CancellationToken.None);
        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
        Assert.True(backup.Success, backup.Error);
        await service.StageRestoreAsync(backup.BackupId!, false, CancellationToken.None);
        var pending = await ReadPendingRestoreAsync(app.Paths.PendingRestorePath);
        File.Delete(Path.Combine(pending.StagingDirectory, "data", "glance.db"));

        var coordinator = new RestoreCoordinator(
            app.Paths,
            CreateHealth(app.Paths),
            NullLogger<RestoreCoordinator>.Instance);
        var result = await coordinator.ApplyPendingRestoreAsync(CancellationToken.None);

        Assert.True(result.Failed);
        Assert.False(File.Exists(app.Paths.PendingRestorePath));
        var evidence = Assert.Single(Directory.EnumerateDirectories(
            app.Paths.RecoveryDirectory,
            "failed-restore-*",
            SearchOption.TopDirectoryOnly));
        Assert.True(File.Exists(Path.Combine(evidence, "pending-restore.json")));
        Assert.True(File.Exists(Path.Combine(evidence, "failure.txt")));

        var restaged = await service.StageRestoreAsync(backup.BackupId!, false, CancellationToken.None);
        Assert.True(restaged.Ready);
    }

    [Fact]
    public async Task RestoreInstallFailure_RollsBackQuarantinesPlanAndAllowsRestaging()
    {
        await using var app = TestAppFixture.Create();
        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Snapshot task"),
            TestAppFixture.CreateContent("First"),
            1,
            null,
            null), CancellationToken.None);
        var service = CreateService(app);
        var backup = await service.CreateBackupAsync("manual", DateTime.Now, CancellationToken.None);
        Assert.True(backup.Success, backup.Error);

        await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Live task"),
            TestAppFixture.CreateContent("Second"),
            2,
            null,
            null), CancellationToken.None);
        await service.StageRestoreAsync(backup.BackupId!, false, CancellationToken.None);
        var pending = await ReadPendingRestoreAsync(app.Paths.PendingRestorePath);

        var coordinator = new RestoreCoordinator(
            app.Paths,
            CreateHealth(app.Paths),
            NullLogger<RestoreCoordinator>.Instance,
            point =>
            {
                if (point == "after-install") throw new IOException("Injected post-install failure.");
            });
        var result = await coordinator.ApplyPendingRestoreAsync(CancellationToken.None);

        Assert.True(result.Failed);
        Assert.False(File.Exists(app.Paths.PendingRestorePath));
        Assert.False(File.Exists(Path.Combine(pending.RescueDirectory, ".original-state-preserved")));
        Assert.Equal(2, await CountTasksAsync(app.Paths));
        Assert.Contains(
            Directory.EnumerateDirectories(app.Paths.RecoveryDirectory, "failed-restore-*", SearchOption.TopDirectoryOnly),
            directory => File.Exists(Path.Combine(directory, "pending-restore.json")));

        var restaged = await service.StageRestoreAsync(backup.BackupId!, false, CancellationToken.None);
        Assert.True(restaged.Ready);
    }

    [Fact]
    public async Task StartupIntegrityFailure_DoesNotRebuildOrReplaceLiveDatabase()
    {
        await using var app = TestAppFixture.Create();
        SqliteConnection.ClearAllPools();
        var corruptBytes = "deliberately corrupt live database"u8.ToArray();
        await File.WriteAllBytesAsync(app.Paths.DatabasePath, corruptBytes);

        var stateStore = new MaintenanceStateStore(app.Paths);
        var health = CreateHealth(app.Paths);
        var settings = new DataSafetySettingsStore(app.Paths);
        var attachments = new AttachmentMaintenance(app.Paths, NullLogger<AttachmentMaintenance>.Instance);
        var dataSafety = new DataSafetyService(
            app.Paths,
            health,
            settings,
            stateStore,
            app.Tasks,
            attachments,
            NullLogger<DataSafetyService>.Instance);
        var restore = new RestoreCoordinator(app.Paths, health, NullLogger<RestoreCoordinator>.Instance);
        var initializer = new DatabaseInitializer(app.Paths, NullLogger<DatabaseInitializer>.Instance);
        var coordinator = new DatabaseStartupCoordinator(
            app.Paths,
            initializer,
            health,
            dataSafety,
            restore,
            stateStore,
            NullLogger<DatabaseStartupCoordinator>.Instance);

        var result = await coordinator.PrepareAsync(CancellationToken.None);

        Assert.True(result.RecoveryMode);
        Assert.False(result.Healthy);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(app.Paths.DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(app.Paths.DataDirectory, "*recovered*", SearchOption.TopDirectoryOnly));
    }

    private static DataSafetyService CreateService(TestAppFixture app)
    {
        var state = new MaintenanceStateStore(app.Paths);
        var health = CreateHealth(app.Paths);
        var attachments = new AttachmentMaintenance(app.Paths, NullLogger<AttachmentMaintenance>.Instance);
        return new DataSafetyService(
            app.Paths,
            health,
            new DataSafetySettingsStore(app.Paths),
            state,
            app.Tasks,
            attachments,
            NullLogger<DataSafetyService>.Instance);
    }

    private static DatabaseHealthService CreateHealth(AppPaths paths) =>
        new(paths, NullLogger<DatabaseHealthService>.Instance);

    private static async Task CorruptArchivedDatabaseAsync(string archivePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var database = archive.GetEntry("data/glance.db")!;
        database.Delete();
        var replacement = archive.CreateEntry("data/glance.db");
        await using var writer = new StreamWriter(replacement.Open());
        await writer.WriteAsync("not a sqlite database");
    }

    private static async Task<PendingRestorePlan> ReadPendingRestoreAsync(string path)
    {
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PendingRestorePlan>(input, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Test restore plan was empty.");
    }

    private static async Task<long> CountTasksAsync(AppPaths paths)
    {
        await using var connection = new SqliteConnection(paths.ReadOnlyConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
