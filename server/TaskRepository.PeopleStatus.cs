using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    public async Task<IReadOnlyList<TaskItem>> GetPersonTasksAsync(
        string personId,
        long startOfToday,
        CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at, t.scheduled_date, t.recurrence_json,
                   t.owner_person_id, p.display_name, t.status_input_at, t.origin_label,
                   EXISTS(SELECT 1 FROM task_send_events e WHERE e.source_task_id = t.id AND e.sent_at > COALESCE(t.send_marker_dismissed_at, 0))
            FROM tasks t
            JOIN people p ON p.id = t.owner_person_id
            WHERE t.owner_person_id = $personId
              AND t.page = 'people:main'
              AND t.deleted_at IS NULL
              AND (t.completed_at IS NULL OR t.completed_at >= $startOfToday)
            ORDER BY t.position ASC;
            """;
        command.Parameters.AddWithValue("$personId", personId);
        command.Parameters.AddWithValue("$startOfToday", startOfToday);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }
        return tasks;
    }

    public async Task<IReadOnlyList<TaskItem>> GetStatusInputTasksAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at, t.scheduled_date, t.recurrence_json,
                   t.owner_person_id, p.display_name, t.status_input_at, t.origin_label,
                   EXISTS(SELECT 1 FROM task_send_events e WHERE e.source_task_id = t.id AND e.sent_at > COALESCE(t.send_marker_dismissed_at, 0))
            FROM tasks t
            LEFT JOIN people p ON p.id = t.owner_person_id
            WHERE t.status_input_at IS NOT NULL
              AND t.deleted_at IS NULL
              AND t.page IN ($newPage, $mainPage)
            ORDER BY t.status_input_at ASC;
            """;
        command.Parameters.AddWithValue("$newPage", TaskPages.DashboardNew);
        command.Parameters.AddWithValue("$mainPage", TaskPages.DashboardMain);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }
        return tasks;
    }

    public async Task<TaskStatusMarkersResponse?> SetStatusMarkersAsync(
        string taskId,
        TaskStatusMarkersRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var existing = await GetTaskRowAsync(connection, taskId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (request.BaseUpdatedAt != existing.UpdatedAt)
        {
            throw new TaskWriteConflictException(taskId, existing.UpdatedAt);
        }

        var now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), existing.UpdatedAt + 1);
        var titleText = TaskTextExtractor.ExtractPlainText(request.Title);
        var statusInputAt = StatusMarkerExtractor.GetEarliestTimestamp(request.Title, request.Content);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tasks
                SET title = $title,
                    title_json = $titleJson,
                    content_json = $contentJson,
                    status_input_at = $statusInputAt,
                    updated_at = $updatedAt
                WHERE id = $id
                  AND deleted_at IS NULL
                  AND updated_at = $baseUpdatedAt;
                """;
            command.Parameters.AddWithValue("$title", titleText);
            command.Parameters.AddWithValue("$titleJson", request.Title.GetRawText());
            command.Parameters.AddWithValue("$contentJson", request.Content.GetRawText());
            command.Parameters.AddWithValue("$statusInputAt", (object?)statusInputAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$baseUpdatedAt", request.BaseUpdatedAt);
            var updated = await command.ExecuteNonQueryAsync(cancellationToken);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                var current = await GetTaskRowAsync(connection, taskId, cancellationToken);
                throw new TaskWriteConflictException(taskId, current?.UpdatedAt ?? existing.UpdatedAt);
            }
        }
        await UpdateSearchAsync(connection, transaction, taskId, request.Title, request.Content, cancellationToken);
        await InsertChangeAsync(connection, transaction, taskId, "status-input", now);
        await transaction.CommitAsync(cancellationToken);
        return new TaskStatusMarkersResponse(now, statusInputAt);
    }
}
