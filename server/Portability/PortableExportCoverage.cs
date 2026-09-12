using Microsoft.Data.Sqlite;

namespace Glance.Server.Portability;

/// <summary>
/// The explicit contract for durable data in a Glance portable export.
/// Adding a durable table or column must update this map and the export schema.
/// </summary>
internal static class PortableExportCoverage
{
    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> DurableTables =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["schema_migrations"] = Columns("version", "applied_at"),
            ["tasks"] = Columns(
                "id", "page", "title", "title_json", "content_json", "position",
                "created_at", "updated_at", "completed_at", "scheduled_date",
                "recurrence_json", "owner_person_id", "status_input_at", "origin_label",
                "send_marker_dismissed_at", "deleted_at"),
            ["people"] = Columns("id", "display_name", "position", "created_at", "updated_at", "archived_at"),
            ["person_tags"] = Columns("id", "name", "position", "created_at", "updated_at"),
            ["person_tag_members"] = Columns("person_id", "tag_id"),
            ["task_send_events"] = Columns(
                "id", "source_task_id", "destination_kind", "destination_id",
                "destination_label", "direction", "sent_at"),
            ["status_update_runs"] = Columns(
                "report_date", "report_id", "input_revision", "document_status",
                "created_at", "updated_at", "completed_at", "input_sha256", "relative_json_path"),
            ["app_meta"] = Columns("key", "value")
        };

    internal static readonly IReadOnlyDictionary<string, string> ExcludedTables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["changes"] = "Derived synchronization log; note state is exported from the durable source tables.",
            ["task_search"] = "Derived full-text index; it can be rebuilt from task rich text."
        };

    internal static async Task ValidateDatabaseCoverageAsync(SqliteConnection connection, CancellationToken token)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            if (IsDerivedFtsTable(table) || ExcludedTables.ContainsKey(table))
            {
                continue;
            }

            if (!DurableTables.TryGetValue(table, out var classifiedColumns))
            {
                throw new InvalidOperationException(
                    $"Portable export is not classified for durable table '{table}'. Update PortableExportCoverage before exporting.");
            }

            var actualColumns = await GetColumnsAsync(connection, table, token);
            var unknown = actualColumns.Where(column => !classifiedColumns.Contains(column)).ToArray();
            if (unknown.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Portable export is not classified for {table}.{string.Join($", {table}.", unknown)}. " +
                    "Update PortableExportCoverage and the export schema before exporting.");
            }
        }
    }

    internal static async Task<IReadOnlyList<string>> GetPresentColumnsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken token)
    {
        var actual = await GetColumnsAsync(connection, table, token);
        return DurableTables[table].Where(actual.Contains).ToArray();
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

    private static bool IsDerivedFtsTable(string table) =>
        table.StartsWith("task_search_", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Columns(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
