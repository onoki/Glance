using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed class TaskRepository
{
    private readonly AppPaths _paths;

    public TaskRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<TaskCreateResponse> CreateTaskAsync(TaskCreateRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var taskId = Guid.NewGuid().ToString("D");
        var titleJson = request.Title.GetRawText();
        var titleText = TaskTextExtractor.ExtractPlainText(request.Title);
        var contentJson = request.Content.GetRawText();
        var scheduledDate = request.ScheduledDate.HasValue
            ? NormalizeScheduledDate(ParseScheduledDate(request.ScheduledDate.Value))
            : null;
        var recurrenceJson = NormalizeRecurrenceJson(request.Recurrence);

        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertTaskAsync(connection, transaction, taskId, request.Page, titleText, titleJson, contentJson, request.Position, now, scheduledDate, recurrenceJson);
        await UpdateSearchAsync(connection, transaction, taskId, request.Title, request.Content);
        await InsertChangeAsync(connection, transaction, taskId, "create", now);
        await transaction.CommitAsync(cancellationToken);

        return new TaskCreateResponse(taskId, now);
    }

    public async Task<TaskUpdateResponse?> UpdateTaskAsync(string taskId, TaskUpdateRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var existing = await GetTaskRowAsync(connection, taskId, cancellationToken);
        if (existing == null)
        {
            return null;
        }

        var externalUpdate = request.BaseUpdatedAt < existing.UpdatedAt;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var newTitleJson = request.Title.HasValue
            ? request.Title.Value
            : ParseTitleJson(existing.TitleJson, existing.TitleText);
        var newTitleText = TaskTextExtractor.ExtractPlainText(newTitleJson);
        var newContentJson = request.Content.HasValue ? request.Content.Value.GetRawText() : existing.ContentJson;
        var newPage = request.Page ?? existing.Page;
        var newPosition = request.Position ?? existing.Position;
        var newScheduledDate = request.ScheduledDate.HasValue
            ? NormalizeScheduledDate(ParseScheduledDate(request.ScheduledDate.Value))
            : existing.ScheduledDate;
        var newRecurrenceJson = request.Recurrence.HasValue
            ? NormalizeRecurrenceJson(request.Recurrence)
            : existing.RecurrenceJson;

        JsonElement contentElement;
        if (request.Content.HasValue)
        {
            contentElement = request.Content.Value;
        }
        else
        {
            using var doc = JsonDocument.Parse(existing.ContentJson);
            contentElement = doc.RootElement.Clone();
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await UpdateTaskAsync(connection, transaction, taskId, newPage, newTitleText, newTitleJson.GetRawText(), newContentJson, newPosition, now, newScheduledDate, newRecurrenceJson);
        await UpdateSearchAsync(connection, transaction, taskId, newTitleJson, contentElement);
        await InsertChangeAsync(connection, transaction, taskId, "update", now);
        await transaction.CommitAsync(cancellationToken);

        return new TaskUpdateResponse(now, externalUpdate);
    }

    public async Task<TaskCompleteResponse?> SetCompletionAsync(string taskId, bool completed, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var exists = await TaskExistsAsync(connection, taskId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var completedAt = completed ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : (long?)null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tasks
                SET completed_at = $completedAt,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$completedAt", (object?)completedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.Parameters.AddWithValue("$id", taskId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertChangeAsync(connection, transaction, taskId, "complete", now);
        await transaction.CommitAsync(cancellationToken);

        return new TaskCompleteResponse(completedAt);
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var exists = await TaskExistsAsync(connection, taskId, cancellationToken);
        if (!exists)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var deleteSearch = connection.CreateCommand())
        {
            deleteSearch.Transaction = transaction;
            deleteSearch.CommandText = "DELETE FROM task_search WHERE task_id = $id;";
            deleteSearch.Parameters.AddWithValue("$id", taskId);
            await deleteSearch.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteTask = connection.CreateCommand())
        {
            deleteTask.Transaction = transaction;
            deleteTask.CommandText = "DELETE FROM tasks WHERE id = $id;";
            deleteTask.Parameters.AddWithValue("$id", taskId);
            await deleteTask.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertChangeAsync(connection, transaction, taskId, "delete", now);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksByPageAsync(string page, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, position, created_at, updated_at, completed_at, scheduled_date, recurrence_json
            FROM tasks
            WHERE page = $page AND completed_at IS NULL
            ORDER BY position ASC;
            """;
        command.Parameters.AddWithValue("$page", page);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task<IReadOnlyList<TaskItem>> GetDashboardMainTasksAsync(long startOfToday, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, position, created_at, updated_at, completed_at, scheduled_date, recurrence_json
            FROM tasks
            WHERE page = 'dashboard:main'
              AND (completed_at IS NULL OR completed_at >= $startOfToday)
            ORDER BY position ASC;
            """;
        command.Parameters.AddWithValue("$startOfToday", startOfToday);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task<IReadOnlyList<TaskItem>> GetHistoryTasksAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, position, created_at, updated_at, completed_at, scheduled_date, recurrence_json
            FROM tasks
            WHERE completed_at IS NOT NULL
            ORDER BY completed_at DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task<int> GetTaskCountAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)count : 0;
    }

    public async Task<bool> SearchIndexExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='task_search';";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task RebuildSearchIndexAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var drop = connection.CreateCommand())
        {
            drop.Transaction = transaction;
            drop.CommandText = "DROP TABLE IF EXISTS task_search;";
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = "CREATE VIRTUAL TABLE task_search USING fts5(task_id, content);";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id, title, title_json, content_json
            FROM tasks;
            """;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO task_search (task_id, content) VALUES ($id, $content);";
        var idParam = insert.Parameters.Add("$id", SqliteType.Text);
        var contentParam = insert.Parameters.Add("$content", SqliteType.Text);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var titleText = reader.GetString(1);
            var titleJson = reader.IsDBNull(2) ? null : reader.GetString(2);
            var contentJson = reader.GetString(3);

            var title = ParseTitleJson(titleJson, titleText);
            using var contentDoc = JsonDocument.Parse(contentJson);
            var contentText = TaskTextExtractor.ExtractPlainText(contentDoc.RootElement.Clone());
            var combined = $"{TaskTextExtractor.ExtractPlainText(title)}\n{contentText}";

            idParam.Value = id;
            contentParam.Value = combined;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> MoveCompletedToHistoryAsync(long startOfToday, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var taskIds = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT id
                FROM tasks
                WHERE completed_at IS NOT NULL
                  AND completed_at >= $startOfToday;
                """;
            select.Parameters.AddWithValue("$startOfToday", startOfToday);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                taskIds.Add(reader.GetString(0));
            }
        }

        if (taskIds.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var movedTo = startOfToday - 1;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE tasks
                SET completed_at = $completedAt,
                    updated_at = $updatedAt
                WHERE completed_at IS NOT NULL
                  AND completed_at >= $startOfToday;
                """;
            update.Parameters.AddWithValue("$completedAt", movedTo);
            update.Parameters.AddWithValue("$updatedAt", now);
            update.Parameters.AddWithValue("$startOfToday", startOfToday);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var taskId in taskIds)
        {
            await InsertChangeAsync(connection, transaction, taskId, "complete", now);
        }

        await transaction.CommitAsync(cancellationToken);
        return taskIds.Count;
    }

    public async Task<IReadOnlyList<HistoryDayStat>> GetHistoryStatsAsync(long startOfWindow, CancellationToken cancellationToken)
    {
        var stats = new List<HistoryDayStat>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date(completed_at / 1000, 'unixepoch', 'localtime') AS day, COUNT(*) AS count
            FROM tasks
            WHERE completed_at IS NOT NULL AND completed_at >= $start
            GROUP BY day
            ORDER BY day ASC;
            """;
        command.Parameters.AddWithValue("$start", startOfWindow);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var day = reader.GetString(0);
            var count = reader.GetInt32(1);
            stats.Add(new HistoryDayStat(day, count));
        }

        return stats;
    }

    public async Task<IReadOnlyList<TaskItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var ftsQuery = BuildSearchQuery(query);
        var likeTokens = BuildLikeTokens(query);
        if (string.IsNullOrWhiteSpace(ftsQuery) && likeTokens.Count == 0)
        {
            return results;
        }

        await using var command = connection.CreateCommand();
        var selectBase = """
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at, t.scheduled_date, t.recurrence_json
            FROM task_search ts
            JOIN tasks t ON t.id = ts.task_id
            """;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            parts.Add($"{selectBase} WHERE task_search MATCH $query");
            command.Parameters.AddWithValue("$query", ftsQuery);
        }

        if (likeTokens.Count > 0)
        {
            var likeClauses = new List<string>();
            for (var i = 0; i < likeTokens.Count; i += 1)
            {
                var param = $"$like{i}";
                likeClauses.Add($"lower(ts.content) LIKE {param} ESCAPE '\\'");
                command.Parameters.AddWithValue(param, $"%{EscapeLike(likeTokens[i].ToLowerInvariant())}%");
            }
            parts.Add($"{selectBase} WHERE {string.Join(" AND ", likeClauses)}");
        }

        command.CommandText = $"""
            {string.Join("\nUNION\n", parts)}
            ORDER BY updated_at DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadTask(reader));
        }

        return results;
    }

    public async Task<int> GenerateRecurringTasksAsync(DateTime todayLocal, CancellationToken cancellationToken)
    {
        var startDate = todayLocal.Date;
        var endDate = startDate.AddDays(27);
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sources = await GetRecurringSourcesAsync(connection, cancellationToken);
        if (sources.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var created = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var source in sources)
        {
            var dates = GetOccurrenceDates(source.Recurrence, startDate, endDate);
            foreach (var date in dates)
            {
                var taskId = CreateDeterministicId(source.Id, date);
                var inserted = await InsertGeneratedTaskAsync(
                    connection,
                    transaction,
                    taskId,
                    source.Page,
                    source.TitleText,
                    source.TitleJson,
                    source.ContentJson,
                    now + created,
                    now,
                    date);

                if (!inserted)
                {
                    continue;
                }

                using var titleDoc = JsonDocument.Parse(source.TitleJson);
                using var contentDoc = JsonDocument.Parse(source.ContentJson);
                await UpdateSearchAsync(connection, transaction, taskId, titleDoc.RootElement.Clone(), contentDoc.RootElement.Clone());
                await InsertChangeAsync(connection, transaction, taskId, "create", now);

                created += 1;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    private static string BuildSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var tokens = query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var token in tokens)
        {
            var upper = token.ToUpperInvariant();
            if (upper is "AND" or "OR" or "NOT" or "NEAR")
            {
                parts.Add(token);
                continue;
            }

            if (token.Contains('*') || token.Contains('"'))
            {
                parts.Add(token);
                continue;
            }

            parts.Add($"{token}*");
        }

        return string.Join(' ', parts);
    }

    private static string FormatDateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    private static string CreateDeterministicId(string sourceId, string scheduledDate)
    {
        var input = $"{sourceId}:{scheduledDate}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes).ToString("D");
    }

    private sealed record RecurrenceSpec(
        string Type,
        IReadOnlyList<int> Weekdays,
        IReadOnlyList<int> MonthDays
    );

    private sealed record RecurrenceSource(
        string Id,
        string Page,
        string TitleText,
        string TitleJson,
        string ContentJson,
        RecurrenceSpec Recurrence
    );

    private static IReadOnlyList<string> GetOccurrenceDates(RecurrenceSpec recurrence, DateTime startDate, DateTime endDate)
    {
        var results = new List<string>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (recurrence.Type == "weekly")
            {
                var weekday = DayOfWeekToNumber(date.DayOfWeek);
                if (recurrence.Weekdays.Contains(weekday))
                {
                    results.Add(FormatDateKey(date));
                }
                continue;
            }

            if (recurrence.Type == "monthly")
            {
                if (recurrence.MonthDays.Contains(date.Day))
                {
                    results.Add(FormatDateKey(date));
                }
            }
        }
        return results;
    }

    private static int DayOfWeekToNumber(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            DayOfWeek.Sunday => 7,
            _ => 1
        };
    }

    private static async Task<List<RecurrenceSource>> GetRecurringSourcesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var sources = new List<RecurrenceSource>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, recurrence_json
            FROM tasks
            WHERE recurrence_json IS NOT NULL;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recurrenceJson = reader.GetString(5);
            if (string.IsNullOrWhiteSpace(recurrenceJson))
            {
                continue;
            }
            if (!TryParseRecurrence(recurrenceJson, out var recurrence))
            {
                continue;
            }
            if (recurrence.Type == "repeatable" || recurrence.Type == "notes")
            {
                continue;
            }
            if (recurrence.Type == "weekly" && recurrence.Weekdays.Count == 0)
            {
                continue;
            }
            if (recurrence.Type == "monthly" && recurrence.MonthDays.Count == 0)
            {
                continue;
            }
            sources.Add(new RecurrenceSource(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? ParseTitleJson(null, reader.GetString(2)).GetRawText() : reader.GetString(3),
                reader.GetString(4),
                recurrence));
        }

        return sources;
    }


    private static bool TryParseRecurrence(string recurrenceJson, out RecurrenceSpec recurrence)
    {
        recurrence = new RecurrenceSpec("repeatable", Array.Empty<int>(), Array.Empty<int>());
        try
        {
            using var doc = JsonDocument.Parse(recurrenceJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            var type = typeElement.GetString() ?? string.Empty;
            var weekdays = Array.Empty<int>();
            var monthDays = Array.Empty<int>();

            if (type == "weekly" && root.TryGetProperty("weekdays", out var weekdayElement) && weekdayElement.ValueKind == JsonValueKind.Array)
            {
                weekdays = weekdayElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetInt32())
                    .Where(value => value >= 1 && value <= 7)
                    .Distinct()
                    .ToArray();
            }

            if (type == "monthly" && root.TryGetProperty("monthDays", out var monthElement) && monthElement.ValueKind == JsonValueKind.Array)
            {
                monthDays = monthElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetInt32())
                    .Where(value => value >= 1 && value <= 31)
                    .Distinct()
                    .ToArray();
            }

            recurrence = new RecurrenceSpec(type, weekdays, monthDays);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> BuildLikeTokens(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        var tokens = query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return new List<string>();
        }

        var results = new List<string>();
        foreach (var token in tokens)
        {
            var trimmed = token.Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var upper = trimmed.ToUpperInvariant();
            if (upper is "AND" or "OR" or "NOT" or "NEAR")
            {
                continue;
            }

            results.Add(trimmed.Replace("*", string.Empty));
        }

        return results;
    }

    private static string EscapeLike(string input)
    {
        return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    private static async Task InsertTaskAsync(SqliteConnection connection, SqliteTransaction transaction, string id, string page, string titleText, string titleJson, string contentJson, double position, long now, string? scheduledDate, string? recurrenceJson)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tasks (id, page, title, title_json, content_json, position, created_at, updated_at, scheduled_date, recurrence_json)
            VALUES ($id, $page, $title, $titleJson, $content, $position, $createdAt, $updatedAt, $scheduledDate, $recurrenceJson);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$title", titleText);
        command.Parameters.AddWithValue("$titleJson", titleJson);
        command.Parameters.AddWithValue("$content", contentJson);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$scheduledDate", (object?)scheduledDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurrenceJson", (object?)recurrenceJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> InsertGeneratedTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        string page,
        string titleText,
        string titleJson,
        string contentJson,
        double position,
        long now,
        string scheduledDate)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO tasks (id, page, title, title_json, content_json, position, created_at, updated_at, scheduled_date, recurrence_json)
            VALUES ($id, $page, $title, $titleJson, $content, $position, $createdAt, $updatedAt, $scheduledDate, NULL);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$title", titleText);
        command.Parameters.AddWithValue("$titleJson", titleJson);
        command.Parameters.AddWithValue("$content", contentJson);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$scheduledDate", scheduledDate);
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static async Task UpdateTaskAsync(SqliteConnection connection, SqliteTransaction transaction, string id, string page, string titleText, string titleJson, string contentJson, double position, long now, string? scheduledDate, string? recurrenceJson)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks
            SET page = $page,
                title = $title,
                title_json = $titleJson,
                content_json = $content,
                position = $position,
                scheduled_date = $scheduledDate,
                recurrence_json = $recurrenceJson,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$title", titleText);
        command.Parameters.AddWithValue("$titleJson", titleJson);
        command.Parameters.AddWithValue("$content", contentJson);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$scheduledDate", (object?)scheduledDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurrenceJson", (object?)recurrenceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateSearchAsync(SqliteConnection connection, SqliteTransaction transaction, string id, JsonElement title, JsonElement content)
    {
        var text = $"{TaskTextExtractor.ExtractPlainText(title)}\n{TaskTextExtractor.ExtractPlainText(content)}";

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM task_search WHERE task_id = $id;";
            delete.Parameters.AddWithValue("$id", id);
            await delete.ExecuteNonQueryAsync();
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO task_search (task_id, content) VALUES ($id, $content);";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$content", text);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertChangeAsync(SqliteConnection connection, SqliteTransaction transaction, string taskId, string changeType, long changedAt)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO changes (entity_type, entity_id, change_type, changed_at)
            VALUES ('task', $id, $type, $changedAt);
            """;
        command.Parameters.AddWithValue("$id", taskId);
        command.Parameters.AddWithValue("$type", changeType);
        command.Parameters.AddWithValue("$changedAt", changedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<TaskRow?> GetTaskRowAsync(SqliteConnection connection, string taskId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, position, updated_at, scheduled_date, recurrence_json
            FROM tasks
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TaskRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetDouble(5),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8)
        );
    }

    private static async Task<bool> TaskExistsAsync(SqliteConnection connection, string taskId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    private static TaskItem ReadTask(SqliteDataReader reader)
    {
        var titleJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        var contentJson = reader.GetString(4);
        var title = ParseTitleJson(titleJson, reader.GetString(2));
        using var doc = JsonDocument.Parse(contentJson);
        var content = doc.RootElement.Clone();
        var scheduledDate = reader.IsDBNull(9) ? null : reader.GetString(9);
        var recurrenceJson = reader.IsDBNull(10) ? null : reader.GetString(10);
        JsonElement? recurrence = null;
        if (!string.IsNullOrWhiteSpace(recurrenceJson))
        {
            using var recurrenceDoc = JsonDocument.Parse(recurrenceJson);
            recurrence = recurrenceDoc.RootElement.Clone();
        }

        return new TaskItem(
            reader.GetString(0),
            reader.GetString(1),
            title,
            content,
            reader.GetDouble(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            scheduledDate,
            recurrence
        );
    }

    private sealed record TaskRow(
        string Id,
        string Page,
        string TitleText,
        string? TitleJson,
        string ContentJson,
        double Position,
        long UpdatedAt,
        string? ScheduledDate,
        string? RecurrenceJson
    );

    private static JsonElement ParseTitleJson(string? titleJson, string fallbackText)
    {
        if (!string.IsNullOrWhiteSpace(titleJson))
        {
            using var doc = JsonDocument.Parse(titleJson);
            return doc.RootElement.Clone();
        }

        var fallback = new
        {
            type = "doc",
            content = new[]
            {
                new
                {
                    type = "paragraph",
                    content = string.IsNullOrWhiteSpace(fallbackText)
                        ? Array.Empty<object>()
                        : new[] { new { type = "text", text = fallbackText } }
                }
            }
        };
        var json = JsonSerializer.Serialize(fallback);
        using var fallbackDoc = JsonDocument.Parse(json);
        return fallbackDoc.RootElement.Clone();
    }

    private static string? NormalizeScheduledDate(string? scheduledDate)
    {
        if (string.IsNullOrWhiteSpace(scheduledDate))
        {
            return null;
        }
        return scheduledDate.Trim();
    }

    private static string? ParseScheduledDate(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => null
        };
    }

    private static string? NormalizeRecurrenceJson(JsonElement? recurrence)
    {
        if (!recurrence.HasValue)
        {
            return null;
        }
        if (recurrence.Value.ValueKind == JsonValueKind.Null || recurrence.Value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }
        return recurrence.Value.GetRawText();
    }
}

