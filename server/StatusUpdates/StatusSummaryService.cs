using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Glance.Server.Integrations.AzureDevOps;
using Glance.Server.Integrations.Outlook;
using Json.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Glance.Server.StatusUpdates;

public sealed class StatusSummaryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly TaskRepository _tasks;
    private readonly IAzureDevOpsStatusReader _azureDevOps;
    private readonly IOutlookStatusReader _outlook;
    private readonly StatusIntegrationOptions _options;
    private JsonSchema? _schema;

    public StatusSummaryService(
        AppPaths paths,
        TaskRepository tasks,
        IAzureDevOpsStatusReader azureDevOps,
        IOutlookStatusReader outlook,
        IOptions<StatusIntegrationOptions> options)
    {
        _paths = paths;
        _tasks = tasks;
        _azureDevOps = azureDevOps;
        _outlook = outlook;
        _options = options.Value;
    }

    public async Task<StatusOverviewResponse> GetOverviewAsync(CancellationToken token)
    {
        var latest = await GetLatestRunAsync(token);
        var azure = _azureDevOps.GetConfiguration();
        var outlook = _outlook.GetConfiguration();
        StatusPreview? preview = null;
        if (latest is not null)
        {
            var document = await ReadDocumentAsync(latest, token);
            preview = new StatusPreview(
                document["project"]!["name"]!.GetValue<string>(),
                document["period"]!.Deserialize<StatusPeriod>(JsonOptions)!,
                document["input"]?["glance"]?["items"]?.AsArray().Count ?? 0,
                document["input"]?["azureDevOps"]?["workItems"]?.AsArray().Count ?? 0,
                document["input"]?["outlook"]?["messages"]?.AsArray().Count ?? 0,
                document["output"]!.Deserialize<StatusOutput>(JsonOptions)!);
        }
        return new StatusOverviewResponse(latest, azure.IsConfigured, outlook.IsConfigured, azure.Message, outlook.Message, preview);
    }

    public async Task<StatusRunInfo> CollectAsync(StatusCollectRequest request, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var reportDate = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var existing = await GetRunByDateAsync(reportDate, token);
        var previous = await GetPreviousCompletedRunAsync(reportDate, token);
        var contextRun = string.Equals(existing?.DocumentStatus, "completed", StringComparison.Ordinal) ? existing : previous;
        var previousDocument = contextRun is null ? null : await ReadDocumentAsync(contextRun, token);
        var defaultFrom = contextRun?.CompletedAt is long completedAt
            ? DateTimeOffset.FromUnixTimeMilliseconds(completedAt)
            : now.AddDays(-28);
        var period = new StatusPeriod(
            (request.FromUtc ?? defaultFrom).ToUniversalTime(),
            now,
            (request.ContextFromUtc ?? now.AddDays(-28)).ToUniversalTime());

        var glanceTask = BuildGlanceInputAsync(period, token);
        var azureTask = _azureDevOps.ReadAsync(period, token);
        var outlookTask = _outlook.ReadAsync(period, token);
        await Task.WhenAll(glanceTask, azureTask, outlookTask);

        var input = new JsonObject
        {
            ["previousReport"] = BuildPreviousReport(previousDocument),
            ["glance"] = await glanceTask,
            ["azureDevOps"] = BuildAzureInput(await azureTask),
            ["outlook"] = BuildOutlookInput(await outlookTask)
        };
        var inputHash = ComputeSha256(input);
        var reportId = existing?.ReportId ?? Guid.NewGuid().ToString("D");
        var revision = (existing?.InputRevision ?? 0) + 1;
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName)
            ? string.IsNullOrWhiteSpace(_options.ProjectName) ? "Project" : _options.ProjectName
            : request.ProjectName.Trim();

        var document = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["reportId"] = reportId,
            ["reportDate"] = reportDate,
            ["inputRevision"] = revision,
            ["documentStatus"] = "input",
            ["createdAtUtc"] = existing is null
                ? now.ToString("O")
                : DateTimeOffset.FromUnixTimeMilliseconds(existing.CreatedAt).ToString("O"),
            ["project"] = new JsonObject { ["name"] = projectName },
            ["period"] = new JsonObject
            {
                ["fromUtc"] = period.FromUtc.ToString("O"),
                ["toUtc"] = period.ToUtc.ToString("O"),
                ["contextFromUtc"] = period.ContextFromUtc.ToString("O")
            },
            ["integrity"] = new JsonObject { ["inputSha256"] = inputHash },
            ["input"] = input,
            ["output"] = new JsonObject
            {
                ["completedAtUtc"] = null,
                ["projectStatus"] = string.Empty,
                ["risks"] = new JsonArray(),
                ["openQuestions"] = new JsonArray()
            }
        };

        var validation = await ValidateAsync(document, token);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Generated status document failed validation: {string.Join("; ", validation.Errors)}");
        }

        var relativePath = Path.Combine("data", "status-updates", reportDate, "StatusSummary.json.gz");
        var run = new StatusRunInfo(
            reportDate, reportId, revision, "input",
            existing?.CreatedAt ?? now.ToUnixTimeMilliseconds(),
            now.ToUnixTimeMilliseconds(), null, inputHash, relativePath.Replace('\\', '/'));
        await WriteDocumentAsync(run, document, token);
        await UpsertRunAsync(run, token);
        return run;
    }

    public async Task<(StatusRunInfo Run, byte[] Json)> GetLatestJsonAsync(CancellationToken token)
    {
        var run = await GetLatestRunAsync(token) ?? throw new FileNotFoundException("No status update has been collected yet.");
        var document = await ReadDocumentAsync(run, token);
        return (run, Encoding.UTF8.GetBytes(document.ToJsonString(JsonOptions)));
    }

    public async Task<StatusImportResponse> ImportAsync(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: true);
        var text = await reader.ReadToEndAsync(token);
        if (Encoding.UTF8.GetByteCount(text) > 20 * 1024 * 1024)
        {
            return new StatusImportResponse(default!, new[] { "The JSON file exceeds the 20 MB import limit." });
        }

        JsonObject? document;
        try
        {
            document = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException ex)
        {
            return new StatusImportResponse(default!, new[] { $"Invalid JSON: {ex.Message}" });
        }
        if (document is null)
        {
            return new StatusImportResponse(default!, new[] { "The uploaded JSON root must be an object." });
        }

        var validation = await ValidateAsync(document, token);
        var errors = validation.Errors.ToList();
        if (!validation.IsValid)
        {
            return new StatusImportResponse(default!, errors);
        }
        var reportDate = document["reportDate"]?.GetValue<string>();
        var reportId = document["reportId"]?.GetValue<string>();
        var revision = document["inputRevision"]?.GetValue<int>() ?? 0;
        var run = string.IsNullOrWhiteSpace(reportDate) ? null : await GetRunByDateAsync(reportDate, token);
        if (run is null) errors.Add("The report date does not match a retained Glance status report.");
        else
        {
            if (!string.Equals(run.ReportId, reportId, StringComparison.Ordinal)) errors.Add("The reportId does not match the retained daily report.");
            if (run.InputRevision != revision) errors.Add("This input revision has been superseded by a newer collection.");
            var input = document["input"];
            var actualHash = input is null ? string.Empty : ComputeSha256(input);
            var suppliedHash = document["integrity"]?["inputSha256"]?.GetValue<string>();
            if (!string.Equals(actualHash, run.InputSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(suppliedHash, run.InputSha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("The input section has changed since Glance created the report.");
            }
        }
        if (!string.Equals(document["documentStatus"]?.GetValue<string>(), "completed", StringComparison.Ordinal))
        {
            errors.Add("documentStatus must be 'completed'.");
        }
        var projectStatus = document["output"]?["projectStatus"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(projectStatus)) errors.Add("output.projectStatus must not be blank.");

        if (errors.Count > 0 || run is null)
        {
            return new StatusImportResponse(run!, errors.Distinct().ToList());
        }

        var completedAt = DateTimeOffset.UtcNow;
        document["output"]!["completedAtUtc"] = completedAt.ToString("O");
        var updatedRun = run with
        {
            DocumentStatus = "completed",
            UpdatedAt = completedAt.ToUnixTimeMilliseconds(),
            CompletedAt = completedAt.ToUnixTimeMilliseconds()
        };
        await WriteDocumentAsync(updatedRun, document, token);
        await UpsertRunAsync(updatedRun, token);
        return new StatusImportResponse(updatedRun, Array.Empty<string>());
    }

    public async Task<StatusExportDocument> GetExportDocumentAsync(CancellationToken token)
    {
        var run = await GetLatestRunAsync(token) ?? throw new FileNotFoundException("No status report is available.");
        if (!string.Equals(run.DocumentStatus, "completed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Upload a completed StatusSummary.json before exporting Office files.");
        }
        var document = await ReadDocumentAsync(run, token);
        var output = document["output"]!.Deserialize<StatusOutput>(JsonOptions)
            ?? throw new InvalidOperationException("Status output could not be read.");
        var period = document["period"]!.Deserialize<StatusPeriod>(JsonOptions)
            ?? throw new InvalidOperationException("Status period could not be read.");
        return new StatusExportDocument(
            run.ReportDate,
            document["project"]!["name"]!.GetValue<string>(),
            period,
            output);
    }

    public async Task<StatusValidationResult> ValidateAsync(JsonNode document, CancellationToken token)
    {
        if (_schema is null)
        {
            var schemaText = await File.ReadAllTextAsync(Path.Combine(_paths.AppRoot, "schema", "status-summary.schema.json"), token);
            var schemaNode = JsonNode.Parse(schemaText)?.AsObject()
                ?? throw new InvalidDataException("The status-summary schema is invalid.");
            // Avoid process-global $id registration collisions between app/test instances.
            schemaNode.Remove("$id");
            _schema = JsonSchema.FromText(schemaNode.ToJsonString());
        }
        using var instance = JsonDocument.Parse(document.ToJsonString());
        var results = _schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid) return new StatusValidationResult(true, Array.Empty<string>());
        var errors = new List<string>();
        CollectErrors(results, errors);
        return new StatusValidationResult(false, errors.Count == 0 ? new[] { "The document does not match the status-summary schema." } : errors);
    }

    private async Task<JsonObject> BuildGlanceInputAsync(StatusPeriod period, CancellationToken token)
    {
        var tasks = await _tasks.GetStatusInputTasksAsync(token);
        var items = new JsonArray();
        foreach (var task in tasks)
        {
            var markers = StatusMarkerExtractor.Extract(task.Title, task.Content);
            var markedSections = new JsonArray();
            foreach (var marker in markers)
            {
                markedSections.Add(new JsonObject
                {
                    ["sectionId"] = marker.SectionId,
                    ["text"] = marker.Text,
                    ["raisedAtUtc"] = DateTimeOffset.FromUnixTimeMilliseconds(marker.RaisedAt).ToString("O"),
                    ["isNewSincePreviousReport"] = marker.RaisedAt >= period.FromUtc.ToUnixTimeMilliseconds()
                });
            }
            if (markers.Count == 0) continue;
            items.Add(new JsonObject
            {
                ["id"] = task.Id,
                ["page"] = task.Page,
                ["person"] = task.OwnerPersonName,
                ["title"] = TaskTextExtractor.ExtractPlainText(task.Title),
                ["fullContent"] = TaskTextExtractor.ExtractPlainText(task.Content),
                ["firstRaisedAtUtc"] = DateTimeOffset.FromUnixTimeMilliseconds(markers.Min(item => item.RaisedAt)).ToString("O"),
                ["updatedAtUtc"] = DateTimeOffset.FromUnixTimeMilliseconds(task.UpdatedAt).ToString("O"),
                ["completedAtUtc"] = task.CompletedAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(task.CompletedAt.Value).ToString("O") : null,
                ["markedSections"] = markedSections
            });
        }
        return new JsonObject
        {
            ["collectionStatus"] = "ok",
            ["collectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["error"] = null,
            ["items"] = items
        };
    }

    private static JsonObject BuildAzureInput(StatusSourceResult<AzureStatusData> result) => new()
    {
        ["collectionStatus"] = result.CollectionStatus,
        ["collectedAtUtc"] = result.CollectedAtUtc?.ToString("O"),
        ["error"] = result.Error,
        ["organization"] = result.Data.Organization,
        ["project"] = result.Data.Project,
        ["queryId"] = result.Data.QueryId,
        ["workItems"] = JsonSerializer.SerializeToNode(result.Data.WorkItems, JsonOptions)
    };

    private static JsonObject BuildOutlookInput(StatusSourceResult<OutlookStatusData> result) => new()
    {
        ["collectionStatus"] = result.CollectionStatus,
        ["collectedAtUtc"] = result.CollectedAtUtc?.ToString("O"),
        ["error"] = result.Error,
        ["folder"] = JsonSerializer.SerializeToNode(result.Data.Folder, JsonOptions),
        ["messages"] = JsonSerializer.SerializeToNode(result.Data.Messages, JsonOptions)
    };

    private static JsonNode? BuildPreviousReport(JsonObject? document)
    {
        if (document is null || !string.Equals(document["documentStatus"]?.GetValue<string>(), "completed", StringComparison.Ordinal)) return null;
        var output = document["output"];
        return new JsonObject
        {
            ["reportDate"] = document["reportDate"]?.GetValue<string>(),
            ["projectStatus"] = output?["projectStatus"]?.DeepClone(),
            ["risks"] = output?["risks"]?.DeepClone(),
            ["openQuestions"] = output?["openQuestions"]?.DeepClone()
        };
    }

    private static string ComputeSha256(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, node);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private async Task WriteDocumentAsync(StatusRunInfo run, JsonObject document, CancellationToken token)
    {
        var directory = Path.Combine(_paths.StatusUpdatesDirectory, run.ReportDate);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "StatusSummary.json.gz");
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(document.ToJsonString(JsonOptions).AsMemory(), token);
        }
        File.Move(temporary, target, true);
    }

    private async Task<JsonObject> ReadDocumentAsync(StatusRunInfo run, CancellationToken token)
    {
        var path = Path.Combine(_paths.AppRoot, run.RelativeJsonPath.Replace('/', Path.DirectorySeparatorChar));
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return await JsonNode.ParseAsync(gzip, cancellationToken: token) as JsonObject
            ?? throw new InvalidDataException("Stored status report is invalid.");
    }

    private async Task UpsertRunAsync(StatusRunInfo run, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO status_update_runs(report_date, report_id, input_revision, document_status, created_at, updated_at, completed_at, input_sha256, relative_json_path)
            VALUES($date, $id, $revision, $status, $created, $updated, $completed, $hash, $path)
            ON CONFLICT(report_date) DO UPDATE SET
              report_id = excluded.report_id,
              input_revision = excluded.input_revision,
              document_status = excluded.document_status,
              updated_at = excluded.updated_at,
              completed_at = excluded.completed_at,
              input_sha256 = excluded.input_sha256,
              relative_json_path = excluded.relative_json_path;
            """;
        AddRunParameters(command, run);
        await command.ExecuteNonQueryAsync(token);
    }

    private Task<StatusRunInfo?> GetLatestRunAsync(CancellationToken token) => GetRunAsync("ORDER BY report_date DESC LIMIT 1", null, token);
    private Task<StatusRunInfo?> GetRunByDateAsync(string date, CancellationToken token) => GetRunAsync("WHERE report_date = $date LIMIT 1", date, token);
    private Task<StatusRunInfo?> GetPreviousCompletedRunAsync(string date, CancellationToken token) => GetRunAsync("WHERE report_date < $date AND document_status = 'completed' ORDER BY report_date DESC LIMIT 1", date, token);

    private async Task<StatusRunInfo?> GetRunAsync(string suffix, string? date, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT report_date, report_id, input_revision, document_status, created_at, updated_at, completed_at, input_sha256, relative_json_path FROM status_update_runs {suffix};";
        if (date is not null) command.Parameters.AddWithValue("$date", date);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new StatusRunInfo(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.GetString(7), reader.GetString(8));
    }

    private static void AddRunParameters(SqliteCommand command, StatusRunInfo run)
    {
        command.Parameters.AddWithValue("$date", run.ReportDate);
        command.Parameters.AddWithValue("$id", run.ReportId);
        command.Parameters.AddWithValue("$revision", run.InputRevision);
        command.Parameters.AddWithValue("$status", run.DocumentStatus);
        command.Parameters.AddWithValue("$created", run.CreatedAt);
        command.Parameters.AddWithValue("$updated", run.UpdatedAt);
        command.Parameters.AddWithValue("$completed", (object?)run.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", run.InputSha256);
        command.Parameters.AddWithValue("$path", run.RelativeJsonPath);
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors)
    {
        if (result.Errors is not null)
        {
            foreach (var error in result.Errors)
            {
                errors.Add($"{result.InstanceLocation}: {error.Value}");
            }
        }
        if (result.Details is not null)
        {
            foreach (var detail in result.Details) CollectErrors(detail, errors);
        }
    }
}
