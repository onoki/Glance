using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    public async Task<TaskSendResponse?> SendToPeopleAsync(
        string sourceTaskId,
        TaskSendToPeopleRequest request,
        CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        var source = await GetTaskRowAsync(connection, sourceTaskId, token);
        if (source is null)
        {
            return null;
        }

        var requestedPersonIds = (request.PersonIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var personIds = requestedPersonIds.ToHashSet(StringComparer.Ordinal);
        var tagIds = (request.TagIds ?? Array.Empty<string>()).ToArray();
        if (tagIds.Length > 0)
        {
            await using var membership = connection.CreateCommand();
            var parameters = new List<string>();
            for (var index = 0; index < tagIds.Length; index += 1)
            {
                var parameter = $"$tag{index}";
                parameters.Add(parameter);
                membership.Parameters.AddWithValue(parameter, tagIds[index]);
            }
            membership.CommandText = $"SELECT DISTINCT person_id FROM person_tag_members WHERE tag_id IN ({string.Join(",", parameters)});";
            await using var reader = await membership.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                personIds.Add(reader.GetString(0));
            }
        }

        var recipients = new List<(string Id, string Name)>();
        foreach (var personId in personIds)
        {
            await using var person = connection.CreateCommand();
            person.CommandText = "SELECT display_name FROM people WHERE id = $id AND archived_at IS NULL;";
            person.Parameters.AddWithValue("$id", personId);
            var name = await person.ExecuteScalarAsync(token) as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                recipients.Add((personId, name));
            }
        }
        if (recipients.Count == 0)
        {
            return new TaskSendResponse(0, Array.Empty<string>());
        }

        var title = StatusMarkerExtractor.RemoveMarkers(ParseTitleJson(source.TitleJson, source.TitleText));
        using var contentDocument = JsonDocument.Parse(source.ContentJson);
        var content = StatusMarkerExtractor.RemoveMarkers(contentDocument.RootElement);
        var now = await GetNextSendTimestampAsync(connection, sourceTaskId, token);
        var createdIds = new List<string>();
        var auditDestinations = recipients
            .Where(recipient => requestedPersonIds.Contains(recipient.Id))
            .Select(recipient => (Kind: "person", Id: (string?)recipient.Id, Label: recipient.Name))
            .ToList();
        foreach (var tagId in tagIds.Distinct(StringComparer.Ordinal))
        {
            await using var tag = connection.CreateCommand();
            tag.CommandText = "SELECT name FROM person_tags WHERE id = $id;";
            tag.Parameters.AddWithValue("$id", tagId);
            if (await tag.ExecuteScalarAsync(token) is string name)
            {
                auditDestinations.Add(("tag", tagId, name));
            }
        }
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        for (var index = 0; index < recipients.Count; index += 1)
        {
            var recipient = recipients[index];
            var taskId = Guid.NewGuid().ToString("D");
            await InsertTaskAsync(
                connection,
                transaction,
                taskId,
                TaskPages.PeopleMain,
                TaskTextExtractor.ExtractPlainText(title),
                title.GetRawText(),
                content.GetRawText(),
                now + index,
                now,
                null,
                null,
                recipient.Id,
                null,
                "From Dashboard");
            await UpdateSearchAsync(connection, transaction, taskId, title, content, token);
            await InsertChangeAsync(connection, transaction, taskId, "create", now);
            createdIds.Add(taskId);
        }
        foreach (var destination in auditDestinations)
        {
            await InsertSendEventAsync(connection, transaction, sourceTaskId, destination.Kind, destination.Id, destination.Label, "dashboard-to-people", now);
        }
        await InsertChangeAsync(connection, transaction, sourceTaskId, "send", now);
        await transaction.CommitAsync(token);
        return new TaskSendResponse(createdIds.Count, createdIds);
    }

    public async Task<TaskSendResponse?> SendToDashboardAsync(string sourceTaskId, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        var source = await GetTaskRowAsync(connection, sourceTaskId, token);
        if (source is null)
        {
            return null;
        }

        var sourceName = "People";
        if (!string.IsNullOrWhiteSpace(source.OwnerPersonId))
        {
            await using var person = connection.CreateCommand();
            person.CommandText = "SELECT display_name FROM people WHERE id = $id;";
            person.Parameters.AddWithValue("$id", source.OwnerPersonId);
            sourceName = await person.ExecuteScalarAsync(token) as string ?? sourceName;
        }

        var title = StatusMarkerExtractor.RemoveMarkers(ParseTitleJson(source.TitleJson, source.TitleText));
        using var contentDocument = JsonDocument.Parse(source.ContentJson);
        var content = StatusMarkerExtractor.RemoveMarkers(contentDocument.RootElement);
        var now = await GetNextSendTimestampAsync(connection, sourceTaskId, token);
        var taskId = Guid.NewGuid().ToString("D");
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await InsertTaskAsync(
            connection,
            transaction,
            taskId,
            TaskPages.DashboardNew,
            TaskTextExtractor.ExtractPlainText(title),
            title.GetRawText(),
            content.GetRawText(),
            now,
            now,
            null,
            null,
            null,
            null,
            $"From {sourceName}");
        await UpdateSearchAsync(connection, transaction, taskId, title, content, token);
        await InsertSendEventAsync(connection, transaction, sourceTaskId, "dashboard", null, "Dashboard", "people-to-dashboard", now);
        await InsertChangeAsync(connection, transaction, taskId, "create", now);
        await InsertChangeAsync(connection, transaction, sourceTaskId, "send", now);
        await transaction.CommitAsync(token);
        return new TaskSendResponse(1, new[] { taskId });
    }

    public async Task<IReadOnlyList<TaskSendEventItem>> GetSendEventsAsync(string taskId, CancellationToken token)
    {
        var events = new List<TaskSendEventItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, destination_kind, destination_id, destination_label, direction, sent_at
            FROM task_send_events
            WHERE source_task_id = $taskId
            ORDER BY sent_at DESC;
            """;
        command.Parameters.AddWithValue("$taskId", taskId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            events.Add(new TaskSendEventItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5)));
        }
        return events;
    }

    public async Task<bool> DismissSendMarkerAsync(string taskId, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE tasks SET send_marker_dismissed_at = $now, updated_at = $now WHERE id = $id AND deleted_at IS NULL;";
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$id", taskId);
        var updated = await command.ExecuteNonQueryAsync(token) > 0;
        if (updated)
        {
            await InsertChangeAsync(connection, transaction, taskId, "send-marker", now);
        }
        await transaction.CommitAsync(token);
        return updated;
    }

    private static async Task InsertSendEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceTaskId,
        string destinationKind,
        string? destinationId,
        string destinationLabel,
        string direction,
        long sentAt)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO task_send_events(id, source_task_id, destination_kind, destination_id, destination_label, direction, sent_at)
            VALUES($id, $sourceTaskId, $destinationKind, $destinationId, $destinationLabel, $direction, $sentAt);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$sourceTaskId", sourceTaskId);
        command.Parameters.AddWithValue("$destinationKind", destinationKind);
        command.Parameters.AddWithValue("$destinationId", (object?)destinationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$destinationLabel", destinationLabel);
        command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$sentAt", sentAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> GetNextSendTimestampAsync(SqliteConnection connection, string sourceTaskId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(send_marker_dismissed_at, 0) FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sourceTaskId);
        var dismissed = Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
        return Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), dismissed + 1);
    }
}
