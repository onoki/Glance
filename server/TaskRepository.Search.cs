using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
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

    private static async Task UpdateSearchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        JsonElement title,
        JsonElement content,
        CancellationToken cancellationToken)
    {
        var text = $"{TaskTextExtractor.ExtractPlainText(title)}\n{TaskTextExtractor.ExtractPlainText(content)}";

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM task_search WHERE task_id = $id;";
            delete.Parameters.AddWithValue("$id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO task_search (task_id, content) VALUES ($id, $content);";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$content", text);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
