using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    internal static readonly TimeSpan SoftDeleteRetention = TimeSpan.FromDays(30);

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
        var statusInputAt = StatusMarkerExtractor.GetEarliestTimestamp(request.Title, request.Content);

        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertTaskAsync(connection, transaction, taskId, request.Page, titleText, titleJson, contentJson, request.Position, now, scheduledDate, recurrenceJson, request.OwnerPersonId, statusInputAt, request.OriginLabel);
        await UpdateSearchAsync(connection, transaction, taskId, request.Title, request.Content, cancellationToken);
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

        if (request.BaseUpdatedAt != existing.UpdatedAt)
        {
            throw new TaskWriteConflictException(taskId, existing.UpdatedAt);
        }
        var now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), existing.UpdatedAt + 1);

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
            : request.ScheduledDate.HasValue
                ? null
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

        var newStatusInputAt = StatusMarkerExtractor.GetEarliestTimestamp(newTitleJson, contentElement);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var updated = await UpdateTaskAsync(
            connection, transaction, taskId, newPage, newTitleText, newTitleJson.GetRawText(),
            newContentJson, newPosition, now, newScheduledDate, newRecurrenceJson,
            newStatusInputAt, request.BaseUpdatedAt);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await GetTaskRowAsync(connection, taskId, cancellationToken);
            throw new TaskWriteConflictException(taskId, current?.UpdatedAt ?? existing.UpdatedAt);
        }
        await UpdateSearchAsync(connection, transaction, taskId, newTitleJson, contentElement, cancellationToken);
        await InsertChangeAsync(connection, transaction, taskId, "update", now);
        await transaction.CommitAsync(cancellationToken);

        return new TaskUpdateResponse(now, false);
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
                WHERE id = $id
                  AND deleted_at IS NULL;
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

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken, long? baseUpdatedAt = null)
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
            deleteTask.CommandText = """
                UPDATE tasks
                SET deleted_at = $deletedAt,
                    updated_at = $deletedAt
                WHERE id = $id
                  AND deleted_at IS NULL
                  AND ($baseUpdatedAt IS NULL OR updated_at = $baseUpdatedAt);
                """;
            deleteTask.Parameters.AddWithValue("$deletedAt", now);
            deleteTask.Parameters.AddWithValue("$id", taskId);
            deleteTask.Parameters.AddWithValue("$baseUpdatedAt", (object?)baseUpdatedAt ?? DBNull.Value);
            if (await deleteTask.ExecuteNonQueryAsync(cancellationToken) == 0 && baseUpdatedAt.HasValue)
            {
                deleteTask.CommandText = "SELECT updated_at FROM tasks WHERE id = $id;";
                var current = Convert.ToInt64(await deleteTask.ExecuteScalarAsync(cancellationToken));
                throw new TaskWriteConflictException(taskId, current);
            }
        }

        await InsertChangeAsync(connection, transaction, taskId, "delete", now);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<TaskRestoreResponse?> RestoreTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        string titleText;
        string? titleJson;
        string contentJson;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT title, title_json, content_json
                FROM tasks
                WHERE id = $id
                  AND deleted_at IS NOT NULL;
                """;
            select.Parameters.AddWithValue("$id", taskId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            titleText = reader.GetString(0);
            titleJson = reader.IsDBNull(1) ? null : reader.GetString(1);
            contentJson = reader.GetString(2);
        }

        var title = ParseTitleJson(titleJson, titleText);
        using var contentDocument = JsonDocument.Parse(contentJson);
        var content = contentDocument.RootElement.Clone();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var restore = connection.CreateCommand())
        {
            restore.Transaction = transaction;
            restore.CommandText = """
                UPDATE tasks
                SET deleted_at = NULL,
                    updated_at = $updatedAt
                WHERE id = $id
                  AND deleted_at IS NOT NULL;
                """;
            restore.Parameters.AddWithValue("$updatedAt", now);
            restore.Parameters.AddWithValue("$id", taskId);
            if (await restore.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await UpdateSearchAsync(connection, transaction, taskId, title, content, cancellationToken);
        await InsertChangeAsync(connection, transaction, taskId, "restore", now);
        await transaction.CommitAsync(cancellationToken);
        return new TaskRestoreResponse(now);
    }

    /// <summary>
    /// Permanently removes tasks outside the soft-delete recovery window. The caller must
    /// provide the timestamp of a verified backup; eligible rows are never purged unless
    /// that backup was created after the most recently deleted eligible row.
    /// </summary>
    public async Task<int> PurgeExpiredDeletedTasksAsync(
        DateTimeOffset now,
        DateTimeOffset? latestVerifiedBackupAt,
        CancellationToken cancellationToken)
    {
        var deletedBefore = (now - SoftDeleteRetention).ToUnixTimeMilliseconds();
        var verifiedBackupAt = latestVerifiedBackupAt?.ToUnixTimeMilliseconds();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        long? newestEligibleDeletion;
        await using (var newest = connection.CreateCommand())
        {
            newest.CommandText = """
                SELECT MAX(deleted_at)
                FROM tasks
                WHERE deleted_at IS NOT NULL
                  AND deleted_at < $deletedBefore;
                """;
            newest.Parameters.AddWithValue("$deletedBefore", deletedBefore);
            var value = await newest.ExecuteScalarAsync(cancellationToken);
            newestEligibleDeletion = value is null or DBNull ? null : Convert.ToInt64(value);
        }

        if (!newestEligibleDeletion.HasValue)
        {
            return 0;
        }

        if (!verifiedBackupAt.HasValue || verifiedBackupAt.Value <= newestEligibleDeletion.Value)
        {
            throw new InvalidOperationException("A verified backup containing the deleted tasks is required before permanent deletion.");
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var deleteSearch = connection.CreateCommand())
        {
            deleteSearch.Transaction = transaction;
            deleteSearch.CommandText = """
                DELETE FROM task_search
                WHERE task_id IN (
                    SELECT id FROM tasks
                    WHERE deleted_at IS NOT NULL AND deleted_at < $deletedBefore
                );
                """;
            deleteSearch.Parameters.AddWithValue("$deletedBefore", deletedBefore);
            await deleteSearch.ExecuteNonQueryAsync(cancellationToken);
        }

        // Do this explicitly as well as relying on the foreign-key cascade. It keeps the
        // purge safe for databases created by older builds where foreign keys were disabled.
        await using (var deleteSendEvents = connection.CreateCommand())
        {
            deleteSendEvents.Transaction = transaction;
            deleteSendEvents.CommandText = """
                DELETE FROM task_send_events
                WHERE source_task_id IN (
                    SELECT id FROM tasks
                    WHERE deleted_at IS NOT NULL AND deleted_at < $deletedBefore
                );
                """;
            deleteSendEvents.Parameters.AddWithValue("$deletedBefore", deletedBefore);
            await deleteSendEvents.ExecuteNonQueryAsync(cancellationToken);
        }

        int purged;
        await using (var deleteTasks = connection.CreateCommand())
        {
            deleteTasks.Transaction = transaction;
            deleteTasks.CommandText = """
                DELETE FROM tasks
                WHERE deleted_at IS NOT NULL
                  AND deleted_at < $deletedBefore;
                """;
            deleteTasks.Parameters.AddWithValue("$deletedBefore", deletedBefore);
            purged = await deleteTasks.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return purged;
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksByPageAsync(
        string page,
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
            LEFT JOIN people p ON p.id = t.owner_person_id
            WHERE t.page = $page
              AND t.deleted_at IS NULL
              AND (t.completed_at IS NULL OR t.completed_at >= $startOfToday)
            ORDER BY t.position ASC;
            """;
        command.Parameters.AddWithValue("$page", page);
        command.Parameters.AddWithValue("$startOfToday", startOfToday);

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
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at, t.scheduled_date, t.recurrence_json,
                   t.owner_person_id, p.display_name, t.status_input_at, t.origin_label,
                   EXISTS(SELECT 1 FROM task_send_events e WHERE e.source_task_id = t.id AND e.sent_at > COALESCE(t.send_marker_dismissed_at, 0))
            FROM tasks t
            LEFT JOIN people p ON p.id = t.owner_person_id
            WHERE t.page = $page
              AND t.deleted_at IS NULL
              AND (t.completed_at IS NULL OR t.completed_at >= $startOfToday)
            ORDER BY t.position ASC;
            """;
        command.Parameters.AddWithValue("$page", TaskPages.DashboardMain);
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
            SELECT t.id, t.page, t.title, t.title_json, t.content_json, t.position, t.created_at, t.updated_at, t.completed_at, t.scheduled_date, t.recurrence_json,
                   t.owner_person_id, p.display_name, t.status_input_at, t.origin_label,
                   EXISTS(SELECT 1 FROM task_send_events e WHERE e.source_task_id = t.id AND e.sent_at > COALESCE(t.send_marker_dismissed_at, 0))
            FROM tasks t
            LEFT JOIN people p ON p.id = t.owner_person_id
            WHERE t.completed_at IS NOT NULL
              AND t.deleted_at IS NULL
            ORDER BY t.completed_at DESC;
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
        command.CommandText = "SELECT COUNT(*) FROM tasks WHERE deleted_at IS NULL;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)count : 0;
    }
}

