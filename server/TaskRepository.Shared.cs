using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    private static async Task InsertTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        string page,
        string titleText,
        string titleJson,
        string contentJson,
        double position,
        long now,
        string? scheduledDate,
        string? recurrenceJson)
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

    private static async Task UpdateTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        string page,
        string titleText,
        string titleJson,
        string contentJson,
        double position,
        long now,
        string? scheduledDate,
        string? recurrenceJson)
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

    private static async Task InsertChangeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        string changeType,
        long changedAt)
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
