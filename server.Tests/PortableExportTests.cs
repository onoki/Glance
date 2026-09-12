using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Glance.Server;
using Glance.Server.Portability;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Server.Tests;

public sealed class PortableExportTests
{
    [Fact]
    public async Task Export_ContainsLosslessDataReadableHtmlMediaAndVerifiedManifest()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Portable note"),
            TestAppFixture.CreateContent("Keep this text"),
            1,
            null,
            null), CancellationToken.None);

        Directory.CreateDirectory(app.Paths.AttachmentsDirectory);
        await File.WriteAllBytesAsync(Path.Combine(app.Paths.AttachmentsDirectory, "sample.png"), new byte[] { 1, 2, 3, 4 });

        var service = new PortableExportService(app.Paths, NullLogger<PortableExportService>.Instance);
        var result = await service.CreateAsync(CancellationToken.None);

        Assert.StartsWith("GlanceExport-v1-", result.FileName);
        using var stream = new MemoryStream(result.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("index.html"));
        Assert.NotNull(archive.GetEntry("dashboard.html"));
        Assert.NotNull(archive.GetEntry("media/attachments/sample.png"));

        using var data = await ReadJsonAsync(archive.GetEntry("data/glance.json")!);
        var tasks = data.RootElement.GetProperty("tables").GetProperty("tasks");
        Assert.Contains(tasks.EnumerateArray(), task =>
            task.GetProperty("id").GetString() == created.TaskId &&
            task.GetProperty("title_json").GetString()!.Contains("Portable note", StringComparison.Ordinal));

        using var manifest = await ReadJsonAsync(archive.GetEntry("manifest.json")!);
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var entry = archive.GetEntry(file.GetProperty("path").GetString()!);
            Assert.NotNull(entry);
            await using var entryStream = entry!.Open();
            using var memory = new MemoryStream();
            await entryStream.CopyToAsync(memory);
            Assert.Equal(file.GetProperty("size").GetInt64(), memory.Length);
            var actualHash = Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
            Assert.Equal(file.GetProperty("sha256").GetString(), actualHash);
        }
    }

    [Fact]
    public async Task Export_RejectsUnclassifiedDurableColumn()
    {
        await using var app = TestAppFixture.Create();
        await using (var connection = new SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE people ADD COLUMN future_durable_note TEXT NULL;";
            await command.ExecuteNonQueryAsync();
        }

        var service = new PortableExportService(app.Paths, NullLogger<PortableExportService>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(CancellationToken.None));
        Assert.Contains("people.future_durable_note", error.Message, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> ReadJsonAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream);
    }
}
