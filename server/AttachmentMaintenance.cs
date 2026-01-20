using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

internal sealed class AttachmentMaintenance
{
    private readonly AppPaths _paths;
    private readonly ILogger<AttachmentMaintenance> _logger;

    public AttachmentMaintenance(AppPaths paths, ILogger<AttachmentMaintenance> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<int> RunAttachmentGcAsync(CancellationToken token)
    {
        try
        {
            var referenced = await CollectReferencedAttachmentsAsync(token);
            if (!Directory.Exists(_paths.AttachmentsDirectory))
            {
                return 0;
            }

            var deleted = 0;
            foreach (var file in Directory.GetFiles(_paths.AttachmentsDirectory))
            {
                var name = Path.GetFileName(file);
                if (!referenced.Contains(name))
                {
                    File.Delete(file);
                    deleted += 1;
                    _logger.LogInformation("Deleted orphaned attachment {File}", name);
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment GC failed");
            return 0;
        }
    }

    private async Task<HashSet<string>> CollectReferencedAttachmentsAsync(CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT content_json FROM tasks;";

        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var json = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }
            using var doc = JsonDocument.Parse(json);
            CollectAttachmentRefs(doc.RootElement, result);
        }

        return result;
    }

    private static void CollectAttachmentRefs(JsonElement element, HashSet<string> refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("type", out var typeElement)
                    && typeElement.ValueKind == JsonValueKind.String
                    && typeElement.GetString() == "image"
                    && element.TryGetProperty("attrs", out var attrs)
                    && attrs.ValueKind == JsonValueKind.Object
                    && attrs.TryGetProperty("src", out var srcElement)
                    && srcElement.ValueKind == JsonValueKind.String)
                {
                    var fileName = ExtractAttachmentFileName(srcElement.GetString());
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        refs.Add(fileName);
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectAttachmentRefs(property.Value, refs);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectAttachmentRefs(child, refs);
                }
                break;
        }
    }

    private static string? ExtractAttachmentFileName(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }
        var index = src.IndexOf("/attachments/", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var path = src[(index + "/attachments/".Length)..];
            path = path.Split('?', '#')[0];
            return Path.GetFileName(path);
        }
        return null;
    }
}
