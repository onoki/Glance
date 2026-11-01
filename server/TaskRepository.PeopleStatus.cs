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
                   EXISTS(SELECT 1 FROM task_send_events e WHERE e.source_task_id = t.id AND e.sent_at > COALESCE(t.send_marker_dismissed_at, 0)), t.is_private, t.history_archived_at
            FROM tasks t
            JOIN people p ON p.id = t.owner_person_id
            WHERE t.owner_person_id = $personId
              AND t.page = 'people:main'
              AND t.deleted_at IS NULL
              AND (t.completed_at IS NULL OR (t.completed_at >= $startOfToday AND t.history_archived_at IS NULL))
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
}
