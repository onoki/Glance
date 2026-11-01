using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    public Task<int> MoveCompletedToHistoryAsync(long startOfToday, CancellationToken cancellationToken) =>
        ArchiveCompletedAsync(startOfToday, previousDays: false, cancellationToken);

    public Task<int> ArchivePreviousCompletionsAsync(long startOfToday, CancellationToken cancellationToken) =>
        ArchiveCompletedAsync(startOfToday, previousDays: true, cancellationToken);

    private async Task<int> ArchiveCompletedAsync(long cutoff, bool previousDays, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ids = new List<string>();
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE tasks
                SET history_archived_at = $now,
                    updated_at = MAX(updated_at + 1, $now)
                WHERE completed_at IS NOT NULL
                  AND completed_at {(previousDays ? "<" : ">=")} $cutoff
                  AND history_archived_at IS NULL
                  AND deleted_at IS NULL
                  AND page IN ($newPage, $mainPage, $peoplePage)
                RETURNING id;
                """;
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$cutoff", cutoff);
            update.Parameters.AddWithValue("$newPage", TaskPages.DashboardNew);
            update.Parameters.AddWithValue("$mainPage", TaskPages.DashboardMain);
            update.Parameters.AddWithValue("$peoplePage", TaskPages.PeopleMain);
            await using var reader = await update.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) ids.Add(reader.GetString(0));
        }
        foreach (var id in ids) await InsertChangeAsync(connection, transaction, id, "archive", now);
        await transaction.CommitAsync(token);
        return ids.Count;
    }

    public async Task<IReadOnlyList<HistoryDayStat>> GetHistoryStatsAsync(long startOfWindow, CancellationToken cancellationToken)
    {
        var stats = new List<HistoryDayStat>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date(completed_at / 1000, 'unixepoch', 'localtime') AS day, COUNT(*) AS count
            FROM tasks
            WHERE completed_at IS NOT NULL
              AND deleted_at IS NULL
              AND completed_at >= $start
            GROUP BY day
            ORDER BY day ASC;
            """;
        command.Parameters.AddWithValue("$start", startOfWindow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) stats.Add(new HistoryDayStat(reader.GetString(0), reader.GetInt32(1)));
        return stats;
    }
}