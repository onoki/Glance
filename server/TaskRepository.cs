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

        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertTaskAsync(connection, transaction, taskId, request.Page, titleText, titleJson, contentJson, request.Position, now);
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
        await UpdateTaskAsync(connection, transaction, taskId, newPage, newTitleText, newTitleJson.GetRawText(), newContentJson, now);
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

    public async Task<IReadOnlyList<TaskItem>> GetTasksByPageAsync(string page, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, position, created_at, updated_at, completed_at
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

    public async Task<IReadOnlyList<TaskItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at
            FROM task_search ts
            JOIN tasks t ON t.id = ts.task_id
            WHERE task_search MATCH $query
            ORDER BY t.updated_at DESC;
            """;
        command.Parameters.AddWithValue("$query", query);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadTask(reader));
        }

        return results;
    }

    private static async Task InsertTaskAsync(SqliteConnection connection, SqliteTransaction transaction, string id, string page, string titleText, string titleJson, string contentJson, double position, long now)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tasks (id, page, title, title_json, content_json, position, created_at, updated_at)
            VALUES ($id, $page, $title, $titleJson, $content, $position, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$title", titleText);
        command.Parameters.AddWithValue("$titleJson", titleJson);
        command.Parameters.AddWithValue("$content", contentJson);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateTaskAsync(SqliteConnection connection, SqliteTransaction transaction, string id, string page, string titleText, string titleJson, string contentJson, long now)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks
            SET page = $page,
                title = $title,
                title_json = $titleJson,
                content_json = $content,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$title", titleText);
        command.Parameters.AddWithValue("$titleJson", titleJson);
        command.Parameters.AddWithValue("$content", contentJson);
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
            SELECT id, page, title, title_json, content_json, updated_at
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
            reader.GetInt64(5)
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

        return new TaskItem(
            reader.GetString(0),
            reader.GetString(1),
            title,
            content,
            reader.GetDouble(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8)
        );
    }

    private sealed record TaskRow(
        string Id,
        string Page,
        string TitleText,
        string? TitleJson,
        string ContentJson,
        long UpdatedAt
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
}

