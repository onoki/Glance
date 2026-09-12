using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Glance.Server.Portability;

internal sealed record PortableExportResult(byte[] Content, string FileName);

internal sealed class PortableExportService
{
    private const string Format = "GlanceExport";
    private const int FormatVersion = 1;
    private readonly AppPaths _paths;
    private readonly ILogger<PortableExportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PortableExportService(AppPaths paths, ILogger<PortableExportService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<PortableExportResult> CreateAsync(CancellationToken token)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var stagingParent = Path.Combine(_paths.DataDirectory, "export-staging");
        var staging = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var snapshotPath = Path.Combine(staging, "glance-export.db");

        try
        {
            await CreateDatabaseSnapshotAsync(snapshotPath, token);
            await using var connection = new SqliteConnection(
                $"Data Source={snapshotPath};Mode=ReadOnly;Cache=Private;Pooling=False;Foreign Keys=True");
            await connection.OpenAsync(token);
            await PortableExportCoverage.ValidateDatabaseCoverageAsync(connection, token);

            var schemaVersion = await GetSchemaVersionAsync(connection, token);
            var tables = new JsonObject();
            foreach (var table in PortableExportCoverage.DurableTables.Keys)
            {
                if (await TableExistsAsync(connection, table, token))
                {
                    tables[table] = await ReadTableAsync(connection, table, token);
                }
            }

            var dataRoot = new JsonObject
            {
                ["format"] = Format,
                ["formatVersion"] = FormatVersion,
                ["generatedAtUtc"] = generatedAt.ToString("O"),
                ["appVersion"] = BuildInfo.Version,
                ["schemaVersion"] = schemaVersion,
                ["tables"] = tables,
                ["excludedDerivedData"] = new JsonObject(
                    PortableExportCoverage.ExcludedTables.Select(item =>
                        KeyValuePair.Create<string, JsonNode?>(item.Key, item.Value)))
            };

            var entries = new List<ExportEntry>();
            AddBytes(entries, "data/glance.json", Encoding.UTF8.GetBytes(dataRoot.ToJsonString(JsonOptions)));
            AddBytes(entries, "README.txt", Encoding.UTF8.GetBytes(BuildReadme(generatedAt)));
            AddBytes(entries, "index.html", Encoding.UTF8.GetBytes(RenderIndex(generatedAt, tables)));
            AddBytes(entries, "dashboard.html", Encoding.UTF8.GetBytes(RenderDashboard(tables)));
            AddBytes(entries, "people.html", Encoding.UTF8.GetBytes(RenderPeople(tables)));
            AddBytes(entries, "history.html", Encoding.UTF8.GetBytes(RenderHistory(tables)));
            var statusDocuments = await AddStatusDocumentsAsync(entries, token);
            AddBytes(entries, "status-updates.html", Encoding.UTF8.GetBytes(RenderStatusUpdates(statusDocuments)));
            AddAttachmentEntries(entries);

            var manifest = BuildManifest(generatedAt, schemaVersion, entries);
            AddBytes(entries, "manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString(JsonOptions)));

            await using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(item.Path, CompressionLevel.SmallestSize);
                    entry.LastWriteTime = generatedAt;
                    await using var destination = entry.Open();
                    if (item.Bytes is not null)
                    {
                        await destination.WriteAsync(item.Bytes, token);
                    }
                    else
                    {
                        await using var source = new FileStream(
                            item.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read,
                            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await source.CopyToAsync(destination, token);
                    }
                }
            }

            var localStamp = generatedAt.ToLocalTime().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
            return new PortableExportResult(output.ToArray(), $"GlanceExport-v1-{localStamp}.zip");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteStaging(staging);
        }
    }

    private async Task CreateDatabaseSnapshotAsync(string destinationPath, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = new SqliteConnection($"{_paths.ConnectionString};Foreign Keys=True");
        await using var destination = new SqliteConnection(
            $"Data Source={destinationPath};Mode=ReadWriteCreate;Cache=Private;Pooling=False;Foreign Keys=True");
        await source.OpenAsync(token);
        await destination.OpenAsync(token);
        source.BackupDatabase(destination);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(token) is not null;
    }

    private static async Task<JsonArray> ReadTableAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        var columns = await PortableExportCoverage.GetPresentColumnsAsync(connection, table, token);
        var selected = string.Join(", ", columns.Select(column => $"\"{column.Replace("\"", "\"\"")}\""));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {selected} FROM \"{table.Replace("\"", "\"\"")}\";";
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new JsonArray();
        while (await reader.ReadAsync(token))
        {
            var row = new JsonObject();
            for (var index = 0; index < columns.Count; index += 1)
            {
                row[columns[index]] = reader.IsDBNull(index) ? null : ToJsonValue(reader.GetValue(index));
            }
            rows.Add(row);
        }
        return rows;
    }

    private static JsonNode? ToJsonValue(object value) => value switch
    {
        long number => JsonValue.Create(number),
        int number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        float number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    private async Task<IReadOnlyList<(string Path, JsonObject Document)>> AddStatusDocumentsAsync(
        List<ExportEntry> entries,
        CancellationToken token)
    {
        var documents = new List<(string Path, JsonObject Document)>();
        if (!Directory.Exists(_paths.StatusUpdatesDirectory))
        {
            return documents;
        }

        foreach (var source in Directory.EnumerateFiles(_paths.StatusUpdatesDirectory, "*.json.gz", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(_paths.StatusUpdatesDirectory, source).Replace('\\', '/');
            if (!IsSafeRelativePath(relative))
            {
                throw new InvalidDataException($"Unsafe status update path in export: {relative}");
            }
            await using var input = File.OpenRead(source);
            await using var gzip = new GZipStream(input, CompressionMode.Decompress);
            await using var buffer = new MemoryStream();
            await gzip.CopyToAsync(buffer, token);
            var bytes = buffer.ToArray();
            AddBytes(entries, $"status-updates/{relative[..^3]}", bytes);
            try
            {
                if (JsonNode.Parse(bytes) is JsonObject document)
                {
                    documents.Add((relative[..^3], document));
                }
            }
            catch (JsonException)
            {
                // The raw file remains part of the lossless export. Backup verification reports invalid status JSON separately.
            }
        }
        return documents;
    }

    private void AddAttachmentEntries(List<ExportEntry> entries)
    {
        if (!Directory.Exists(_paths.AttachmentsDirectory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateFiles(_paths.AttachmentsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(source);
            if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException("An attachment has an unsafe filename.");
            }
            var info = new FileInfo(source);
            entries.Add(new ExportEntry($"media/attachments/{name}", source, null, info.Length, ComputeSha256(source)));
        }
    }

    private static JsonObject BuildManifest(DateTimeOffset generatedAt, int schemaVersion, IReadOnlyList<ExportEntry> entries)
    {
        var files = new JsonArray();
        foreach (var entry in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            files.Add(new JsonObject
            {
                ["path"] = entry.Path,
                ["size"] = entry.Size,
                ["sha256"] = entry.Sha256
            });
        }
        return new JsonObject
        {
            ["format"] = Format,
            ["formatVersion"] = FormatVersion,
            ["generatedAtUtc"] = generatedAt.ToString("O"),
            ["appVersion"] = BuildInfo.Version,
            ["schemaVersion"] = schemaVersion,
            ["hashAlgorithm"] = "SHA-256",
            ["files"] = files
        };
    }

    private static string BuildReadme(DateTimeOffset generatedAt) => $"""
        Glance portable export, format version {FormatVersion}
        Created: {generatedAt:O}

        Start with index.html for a readable offline view.
        data/glance.json is the lossless machine-readable record. It intentionally retains
        rich-text JSON, ordering, timestamps, archived people, soft-deleted notes, tags,
        send events, recurrence settings, and status-update metadata.

        manifest.json lists the SHA-256 hash and byte length of every payload file so an
        importer can verify that the bundle is complete and unchanged.
        """;

    private static string RenderIndex(DateTimeOffset generatedAt, JsonObject tables)
    {
        var taskCount = GetRows(tables, "tasks").Count;
        var personCount = GetRows(tables, "people").Count;
        return HtmlPage("Glance export", $"""
            <h1>Glance export</h1>
            <p>Created {Html(generatedAt.ToString("O"))}</p>
            <ul>
              <li><a href="dashboard.html">Dashboard notes</a> ({taskCount})</li>
              <li><a href="people.html">People notes</a> ({personCount} people)</li>
              <li><a href="history.html">Completed notes</a></li>
              <li><a href="status-updates.html">Status updates</a></li>
              <li><a href="data/glance.json">Machine-readable data</a></li>
            </ul>
            <p>Status report JSON files are in the <code>status-updates</code> folder and images are in <code>media/attachments</code>.</p>
            """);
    }

    private static string RenderDashboard(JsonObject tables)
    {
        var tasks = GetRowsEnumerable(tables, "tasks")
            .Where(IsVisible)
            .Where(task => GetLong(task, "completed_at") is null && string.IsNullOrWhiteSpace(GetString(task, "owner_person_id")))
            .OrderBy(task => GetString(task, "page"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => GetDouble(task, "position"));
        return HtmlPage("Dashboard - Glance export", "<h1>Dashboard</h1>" + RenderTaskGroups(tasks, "page"));
    }

    private static string RenderPeople(JsonObject tables)
    {
        var tasks = GetRowsEnumerable(tables, "tasks").Where(IsVisible).ToLookup(task => GetString(task, "owner_person_id") ?? string.Empty);
        var people = GetRowsEnumerable(tables, "people").OrderBy(person => GetLong(person, "archived_at") is not null).ThenBy(person => GetDouble(person, "position"));
        var body = new StringBuilder("<h1>People</h1>");
        foreach (var person in people)
        {
            var id = GetString(person, "id") ?? string.Empty;
            var archived = GetLong(person, "archived_at") is not null ? " <small>(archived)</small>" : string.Empty;
            body.Append("<section><h2>").Append(Html(GetString(person, "display_name") ?? "Unnamed person")).Append(archived).Append("</h2>");
            body.Append(RenderTasks(tasks[id].OrderBy(task => GetDouble(task, "position"))));
            body.Append("</section>");
        }
        return HtmlPage("People - Glance export", body.ToString());
    }

    private static string RenderHistory(JsonObject tables)
    {
        var tasks = GetRowsEnumerable(tables, "tasks")
            .Where(IsVisible)
            .Where(task => GetLong(task, "completed_at") is not null)
            .OrderByDescending(task => GetLong(task, "completed_at"));
        return HtmlPage("History - Glance export", "<h1>History</h1>" + RenderTasks(tasks));
    }

    private static string RenderStatusUpdates(IReadOnlyList<(string Path, JsonObject Document)> documents)
    {
        var body = new StringBuilder("<h1>Status updates</h1>");
        foreach (var (path, document) in documents.OrderByDescending(item => item.Path, StringComparer.Ordinal))
        {
            var reportDate = document["reportDate"]?.GetValue<string>() ?? path;
            var project = document["project"]?["name"]?.GetValue<string>() ?? "Project";
            var output = document["output"] as JsonObject;
            body.Append("<section><h2>").Append(Html(reportDate)).Append(" · ").Append(Html(project)).Append("</h2>");
            body.Append("<h3>Project status</h3><p>").Append(Html(output?["projectStatus"]?.GetValue<string>() ?? "No completed output")).Append("</p>");
            body.Append(RenderStatusList("Risks", output?["risks"] as JsonArray, "title", "description"));
            body.Append(RenderStatusList("Open questions", output?["openQuestions"] as JsonArray, "question", null));
            body.Append("<p><a href=\"status-updates/").Append(HtmlAttribute(path)).Append("\">Source JSON</a></p></section>");
        }
        if (documents.Count == 0) body.Append("<p>No status update packages were present.</p>");
        return HtmlPage("Status updates - Glance export", body.ToString());
    }

    private static string RenderStatusList(string heading, JsonArray? values, string primaryField, string? secondaryField)
    {
        var body = new StringBuilder("<h3>").Append(Html(heading)).Append("</h3><ul>");
        if (values is not null)
        {
            foreach (var value in values.OfType<JsonObject>())
            {
                body.Append("<li>").Append(Html(value[primaryField]?.GetValue<string>() ?? string.Empty));
                if (secondaryField is not null && value[secondaryField]?.GetValue<string>() is { Length: > 0 } secondary)
                {
                    body.Append(" — ").Append(Html(secondary));
                }
                if (value["isNew"]?.GetValue<bool>() == true) body.Append(" <strong>(new)</strong>");
                body.Append("</li>");
            }
        }
        body.Append("</ul>");
        return body.ToString();
    }

    private static string RenderTaskGroups(IEnumerable<JsonObject> tasks, string field)
    {
        var body = new StringBuilder();
        foreach (var group in tasks.GroupBy(task => GetString(task, field) ?? "Uncategorised"))
        {
            body.Append("<section><h2>").Append(Html(group.Key)).Append("</h2>").Append(RenderTasks(group)).Append("</section>");
        }
        return body.ToString();
    }

    private static string RenderTasks(IEnumerable<JsonObject> tasks)
    {
        var body = new StringBuilder("<ol class=notes>");
        foreach (var task in tasks)
        {
            body.Append("<li class=note><div class=title>")
                .Append(RenderRichText(GetString(task, "title_json")))
                .Append("</div><div class=content>")
                .Append(RenderRichText(GetString(task, "content_json")))
                .Append("</div><div class=meta>")
                .Append(Html(FormatTimestamp(GetLong(task, "created_at"))))
                .Append(" · updated ").Append(Html(FormatTimestamp(GetLong(task, "updated_at"))))
                .Append("</div></li>");
        }
        body.Append("</ol>");
        return body.ToString();
    }

    private static string RenderRichText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return RenderNode(document.RootElement);
        }
        catch (JsonException)
        {
            return $"<pre>{Html(json)}</pre>";
        }
    }

    private static string RenderNode(JsonElement node)
    {
        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
        var children = node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Select(RenderNode))
            : string.Empty;
        return type switch
        {
            "doc" => children,
            "paragraph" => $"<p>{children}</p>",
            "bulletList" => $"<ul>{children}</ul>",
            "orderedList" => $"<ol>{children}</ol>",
            "listItem" => $"<li>{children}</li>",
            "hardBreak" => "<br>",
            "image" => RenderImage(node),
            "text" => RenderText(node),
            _ => children
        };
    }

    private static string RenderText(JsonElement node)
    {
        var value = node.TryGetProperty("text", out var text) ? Html(text.GetString() ?? string.Empty) : string.Empty;
        if (!node.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array) return value;
        foreach (var mark in marks.EnumerateArray())
        {
            var type = mark.TryGetProperty("type", out var markType) ? markType.GetString() : null;
            value = type switch
            {
                "bold" => $"<strong>{value}</strong>",
                "italic" => $"<em>{value}</em>",
                "strike" => $"<s>{value}</s>",
                "code" => $"<code>{value}</code>",
                "link" => RenderLink(mark, value),
                _ => value
            };
        }
        return value;
    }

    private static string RenderLink(JsonElement mark, string body)
    {
        if (!mark.TryGetProperty("attrs", out var attrs) || !attrs.TryGetProperty("href", out var hrefNode)) return body;
        var href = hrefNode.GetString();
        return IsSafeExportLink(href) ? $"<a href=\"{HtmlAttribute(href!)}\">{body}</a>" : body;
    }

    private static string RenderImage(JsonElement node)
    {
        if (!node.TryGetProperty("attrs", out var attrs) || !attrs.TryGetProperty("src", out var srcNode)) return string.Empty;
        var src = srcNode.GetString() ?? string.Empty;
        var slash = src.LastIndexOf('/');
        var fileName = Path.GetFileName(slash >= 0 ? src[(slash + 1)..] : src);
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var alt = attrs.TryGetProperty("alt", out var altNode) ? altNode.GetString() ?? "" : "";
        return $"<img src=\"media/attachments/{HtmlAttribute(fileName)}\" alt=\"{HtmlAttribute(alt)}\">";
    }

    private static string HtmlPage(string title, string body) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{Html(title)}}</title><style>
        body{max-width:72rem;margin:2rem auto;padding:0 1.2rem;font:16px/1.5 system-ui,sans-serif;color:#1d2430;background:#fff}
        nav,a{color:#075985}section{margin:2rem 0}.notes{padding-left:1.6rem}.note{margin:1rem 0;padding:.8rem 1rem;border:1px solid #d7dde5;border-radius:.4rem}
        .title>p:first-child,.content>p:first-child{margin-top:0}.meta,small{color:#667085;font-size:.8rem}img{max-width:100%;height:auto}code{background:#eef2f6;padding:.1rem .25rem}
        </style></head><body><nav><a href="index.html">Export home</a></nav>{{body}}</body></html>
        """;

    private static JsonArray GetRows(JsonObject tables, string table) => tables[table] as JsonArray ?? new JsonArray();
    private static IEnumerable<JsonObject> GetRowsEnumerable(JsonObject tables, string table) => GetRows(tables, table).OfType<JsonObject>();
    private static bool IsVisible(JsonObject task) => GetLong(task, "deleted_at") is null;
    private static string? GetString(JsonObject row, string key) => row[key]?.GetValue<string>();
    private static long? GetLong(JsonObject row, string key) => row[key] is null ? null : row[key]!.GetValue<long>();
    private static double GetDouble(JsonObject row, string key)
    {
        if (row[key] is not JsonValue value) return 0;
        if (value.TryGetValue<double>(out var doubleValue)) return doubleValue;
        return value.TryGetValue<long>(out var longValue) ? longValue : 0;
    }

    private static string FormatTimestamp(long? value) => value is null
        ? "unknown time"
        : DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static bool IsSafeExportLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Any(char.IsControl)) return false;
        if (href.StartsWith("\\\\", StringComparison.Ordinal) ||
            (href.Length >= 3 && char.IsLetter(href[0]) && href[1] == ':' && (href[2] == '\\' || href[2] == '/'))) return true;
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part is not "" and not "." and not "..");

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
    private static string HtmlAttribute(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static void AddBytes(List<ExportEntry> entries, string path, byte[] bytes) =>
        entries.Add(new ExportEntry(path, null, bytes, bytes.LongLength, ComputeSha256(bytes)));

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to remove portable-export staging folder {Path}", staging);
        }
    }

    private sealed record ExportEntry(string Path, string? SourcePath, byte[]? Bytes, long Size, string Sha256);
}
