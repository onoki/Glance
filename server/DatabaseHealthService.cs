using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

internal sealed class DatabaseHealthService
{
    private readonly AppPaths _paths;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(AppPaths paths, ILogger<DatabaseHealthService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public Task<DatabaseHealthResult> CheckLiveDatabaseAsync(CancellationToken token) =>
        CheckDatabaseAsync(_paths.DatabasePath, token);

    public async Task<DatabaseHealthResult> CheckDatabaseAsync(string databasePath, CancellationToken token)
    {
        var errors = new List<string>();
        var supportedVersion = GetSupportedSchemaVersion();
        if (!File.Exists(databasePath))
        {
            errors.Add("The database file does not exist.");
            return new DatabaseHealthResult(false, 0, supportedVersion, 0, errors);
        }

        if (new FileInfo(databasePath).Length == 0)
        {
            errors.Add("The database file is empty.");
            return new DatabaseHealthResult(false, 0, supportedVersion, 0, errors);
        }

        var schemaVersion = 0;
        var lastChangeId = 0L;
        try
        {
            var connectionString = $"Data Source={databasePath};Mode=ReadOnly;Cache=Private;Pooling=False;Foreign Keys=True;Default Timeout=5";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(token);

            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                await using var reader = await integrity.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var message = reader.GetString(0);
                    if (!string.Equals(message, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"SQLite integrity check: {message}");
                    }
                }
            }

            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await foreignKeys.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var table = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
                    var rowId = reader.IsDBNull(1) ? "unknown" : reader.GetValue(1).ToString();
                    errors.Add($"Foreign-key violation in {table}, row {rowId}.");
                    if (errors.Count >= 100)
                    {
                        errors.Add("Additional foreign-key violations were omitted.");
                        break;
                    }
                }
            }

            if (await TableExistsAsync(connection, "schema_migrations", token))
            {
                schemaVersion = (int)await ExecuteLongAsync(
                    connection,
                    "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;",
                    token);
            }

            if (schemaVersion > supportedVersion)
            {
                errors.Add($"Database schema {schemaVersion} is newer than this app supports ({supportedVersion}).");
            }

            if (!await TableExistsAsync(connection, "tasks", token))
            {
                errors.Add("Required table 'tasks' is missing.");
            }
            else
            {
                await ValidateRequiredTaskColumnsAsync(connection, schemaVersion, token, errors);
                await ValidateRichJsonAsync(connection, token, errors);
            }

            await ValidateRequiredTablesAsync(connection, schemaVersion, token, errors);

            if (await TableExistsAsync(connection, "changes", token))
            {
                lastChangeId = await ExecuteLongAsync(
                    connection,
                    "SELECT COALESCE(MAX(id), 0) FROM changes;",
                    token);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(ex, "Database health check failed for {DatabasePath}", databasePath);
            errors.Add($"Unable to read the database safely: {ex.Message}");
        }

        return new DatabaseHealthResult(errors.Count == 0, schemaVersion, supportedVersion, lastChangeId, errors);
    }

    internal int GetSupportedSchemaVersion()
    {
        if (!Directory.Exists(_paths.MigrationsDirectory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(_paths.MigrationsDirectory, "*.sql")
            .Select(path => Path.GetFileName(path))
            .Select(name => new string(name.TakeWhile(char.IsDigit).ToArray()))
            .Select(value => int.TryParse(value, out var version) ? version : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    internal static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type IN ('table', 'view') AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(token) is not null;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken token)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static async Task ValidateRichJsonAsync(
        SqliteConnection connection,
        CancellationToken token,
        List<string> errors)
    {
        var columns = await GetColumnsAsync(connection, "tasks", token);
        var jsonColumns = new List<string>();
        if (columns.Contains("title_json")) jsonColumns.Add("title_json");
        if (columns.Contains("content_json")) jsonColumns.Add("content_json");
        if (jsonColumns.Count == 0)
        {
            errors.Add("The tasks table has no rich-content JSON columns.");
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, {string.Join(", ", jsonColumns)} FROM tasks;";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var id = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
            for (var index = 0; index < jsonColumns.Count; index += 1)
            {
                if (reader.IsDBNull(index + 1))
                {
                    // title_json was nullable in the migration from legacy data.
                    continue;
                }
                try
                {
                    using var _ = JsonDocument.Parse(reader.GetString(index + 1));
                }
                catch (JsonException ex)
                {
                    errors.Add($"Task {id} contains invalid {jsonColumns[index]}: {ex.Message}");
                    if (errors.Count >= 100)
                    {
                        errors.Add("Additional rich-content errors were omitted.");
                        return;
                    }
                }
            }
        }
    }

    private static async Task ValidateRequiredTablesAsync(
        SqliteConnection connection,
        int schemaVersion,
        CancellationToken token,
        List<string> errors)
    {
        var required = new List<string>();
        if (schemaVersion >= 1) required.AddRange(["schema_migrations", "changes"]);
        if (schemaVersion >= 3) required.Add("app_meta");
        if (schemaVersion >= 4)
        {
            required.AddRange([
                "people",
                "person_tags",
                "person_tag_members",
                "task_send_events",
                "status_update_runs"
            ]);
        }

        foreach (var table in required)
        {
            if (!await TableExistsAsync(connection, table, token))
            {
                errors.Add($"Required table '{table}' is missing for schema {schemaVersion}.");
            }
        }
    }

    private static async Task ValidateRequiredTaskColumnsAsync(
        SqliteConnection connection,
        int schemaVersion,
        CancellationToken token,
        List<string> errors)
    {
        var required = new List<string>
        {
            "id", "page", "title", "content_json", "position", "created_at",
            "updated_at", "completed_at", "scheduled_date", "recurrence_json"
        };
        if (schemaVersion >= 2) required.Add("title_json");
        if (schemaVersion >= 4)
        {
            required.AddRange([
                "owner_person_id",
                "status_input_at",
                "origin_label",
                "send_marker_dismissed_at"
            ]);
        }
        if (schemaVersion >= 5) required.Add("deleted_at");

        var actual = await GetColumnsAsync(connection, "tasks", token);
        foreach (var column in required)
        {
            if (!actual.Contains(column))
            {
                errors.Add($"Required tasks.{column} column is missing for schema {schemaVersion}.");
            }
        }
    }

    private static async Task<long> ExecuteLongAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }
}
