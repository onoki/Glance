using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    public async Task<int> MoveCompletedToHistoryAsync(long startOfToday, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var taskIds = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT id
                FROM tasks
                WHERE completed_at IS NOT NULL
                  AND completed_at >= $startOfToday;
                """;
            select.Parameters.AddWithValue("$startOfToday", startOfToday);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                taskIds.Add(reader.GetString(0));
            }
        }

        if (taskIds.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var movedTo = startOfToday - 1;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE tasks
                SET completed_at = $completedAt,
                    updated_at = $updatedAt
                WHERE completed_at IS NOT NULL
                  AND completed_at >= $startOfToday;
                """;
            update.Parameters.AddWithValue("$completedAt", movedTo);
            update.Parameters.AddWithValue("$updatedAt", now);
            update.Parameters.AddWithValue("$startOfToday", startOfToday);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var taskId in taskIds)
        {
            await InsertChangeAsync(connection, transaction, taskId, "complete", now);
        }

        await transaction.CommitAsync(cancellationToken);
        return taskIds.Count;
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
            WHERE completed_at IS NOT NULL AND completed_at >= $start
            GROUP BY day
            ORDER BY day ASC;
            """;
        command.Parameters.AddWithValue("$start", startOfWindow);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var day = reader.GetString(0);
            var count = reader.GetInt32(1);
            stats.Add(new HistoryDayStat(day, count));
        }

        return stats;
    }
}
