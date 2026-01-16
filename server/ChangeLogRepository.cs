using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed class ChangeLogRepository
{
    private readonly AppPaths _paths;

    public ChangeLogRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ChangesResponse> GetChangesAsync(long sinceId, CancellationToken cancellationToken)
    {
        var changes = new List<ChangeItem>();
        long lastId = sinceId;

        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, entity_type, entity_id, change_type, changed_at
            FROM changes
            WHERE id > $since
            ORDER BY id ASC;
            """;
        command.Parameters.AddWithValue("$since", sinceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            lastId = id;
            changes.Add(new ChangeItem(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)
            ));
        }

        return new ChangesResponse(lastId, changes);
    }
}
