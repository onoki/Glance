using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

internal sealed class AttachmentMaintenance
{
    internal static readonly TimeSpan QuarantineRetention = TimeSpan.FromDays(30);

    private readonly AppPaths _paths;
    private readonly ILogger<AttachmentMaintenance> _logger;

    internal string QuarantineDirectory => Path.Combine(_paths.BlobsDirectory, "attachment-trash");

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
            RestoreReferencedAttachments(referenced, token);
            if (!Directory.Exists(_paths.AttachmentsDirectory))
            {
                return 0;
            }

            var quarantineTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var quarantineFolder = Path.Combine(QuarantineDirectory, quarantineTimestamp);
            var quarantined = 0;
            foreach (var file in Directory.GetFiles(_paths.AttachmentsDirectory))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (!referenced.Contains(name))
                {
                    Directory.CreateDirectory(quarantineFolder);
                    var destination = Path.Combine(quarantineFolder, name);
                    if (File.Exists(destination))
                    {
                        // A collision should be practically impossible (attachment names are
                        // GUIDs), but retaining both files is safer than replacing either one.
                        _logger.LogWarning("Could not quarantine orphaned attachment {File}: destination already exists", name);
                        continue;
                    }

                    File.Move(file, destination);
                    quarantined += 1;
                    _logger.LogInformation("Quarantined orphaned attachment {File}", name);
                }
            }

            return quarantined;
        }
        catch (Exception ex)
        {
            // Parsing every rich-text document is deliberate. If any document cannot be
            // understood, fail closed and retain all attachments rather than guessing.
            _logger.LogError(ex, "Attachment GC failed; no unreferenced attachments were removed");
            return 0;
        }
    }

    internal async Task<int> PurgeExpiredQuarantinedAttachmentsAsync(
        DateTimeOffset now,
        DateTimeOffset? latestVerifiedBackupAt,
        CancellationToken token)
    {
        if (!latestVerifiedBackupAt.HasValue || !Directory.Exists(QuarantineDirectory))
        {
            return 0;
        }

        try
        {
            var referenced = await CollectReferencedAttachmentsAsync(token);
            RestoreReferencedAttachments(referenced, token);
            var cutoff = now - QuarantineRetention;
            var purged = 0;

            foreach (var file in Directory.GetFiles(QuarantineDirectory, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (referenced.Contains(name))
                {
                    continue;
                }

                if (!TryGetQuarantinedAt(file, out var quarantinedAt)
                    || quarantinedAt >= cutoff
                    || latestVerifiedBackupAt.Value <= quarantinedAt)
                {
                    continue;
                }

                File.Delete(file);
                purged += 1;
                _logger.LogInformation("Permanently removed quarantined attachment {File}", name);
            }

            return purged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment quarantine cleanup failed; remaining files were retained");
            return 0;
        }
    }

    private async Task<HashSet<string>> CollectReferencedAttachmentsAsync(CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);

        await using var command = connection.CreateCommand();
        // Soft-deleted tasks remain recoverable for the full retention period, so their
        // attachments are references too. Scan titles as well as subcontent: images are
        // supported in both editors.
        command.CommandText = "SELECT title_json, content_json FROM tasks;";

        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            for (var column = 0; column < 2; column += 1)
            {
                if (reader.IsDBNull(column))
                {
                    continue;
                }

                var json = reader.GetString(column);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }
                using var doc = JsonDocument.Parse(json);
                CollectAttachmentRefs(doc.RootElement, result);
            }
        }

        return result;
    }

    private void RestoreReferencedAttachments(HashSet<string> referenced, CancellationToken token)
    {
        if (!Directory.Exists(QuarantineDirectory))
        {
            return;
        }

        Directory.CreateDirectory(_paths.AttachmentsDirectory);
        foreach (var file in Directory.GetFiles(QuarantineDirectory, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (!referenced.Contains(name))
            {
                continue;
            }

            var destination = Path.Combine(_paths.AttachmentsDirectory, name);
            if (File.Exists(destination))
            {
                // Keep the quarantined copy. A later verified cleanup can resolve a
                // duplicate without risking the active attachment.
                continue;
            }

            File.Move(file, destination);
            _logger.LogInformation("Restored referenced attachment {File} from quarantine", name);
        }
    }

    private bool TryGetQuarantinedAt(string file, out DateTimeOffset quarantinedAt)
    {
        quarantinedAt = default;
        var relative = Path.GetRelativePath(QuarantineDirectory, file);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return long.TryParse(firstSegment, out var milliseconds)
            && TryFromUnixMilliseconds(milliseconds, out quarantinedAt);
    }

    private static bool TryFromUnixMilliseconds(long milliseconds, out DateTimeOffset value)
    {
        try
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
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
