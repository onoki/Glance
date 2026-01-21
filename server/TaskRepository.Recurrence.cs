using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed partial class TaskRepository
{
    public async Task<int> GenerateRecurringTasksAsync(DateTime todayLocal, CancellationToken cancellationToken)
    {
        var startDate = todayLocal.Date;
        var endDate = startDate.AddDays(27);
        var weekStart = GetWeekStart(startDate);
        var weekEnd = weekStart.AddDays(6);
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sources = await GetRecurringSourcesAsync(connection, cancellationToken);
        if (sources.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var created = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var source in sources)
        {
            var dates = GetOccurrenceDates(source.Recurrence, startDate, endDate, weekStart, weekEnd);
            foreach (var date in dates)
            {
                var taskId = CreateDeterministicId(source.Id, date);
                var inserted = await InsertGeneratedTaskAsync(
                    connection,
                    transaction,
                    taskId,
                    source.Page,
                    source.TitleText,
                    source.TitleJson,
                    source.ContentJson,
                    now + created,
                    now,
                    date);

                if (!inserted)
                {
                    continue;
                }

                using var titleDoc = JsonDocument.Parse(source.TitleJson);
                using var contentDoc = JsonDocument.Parse(source.ContentJson);
                await UpdateSearchAsync(connection, transaction, taskId, titleDoc.RootElement.Clone(), contentDoc.RootElement.Clone(), cancellationToken);
                await InsertChangeAsync(connection, transaction, taskId, "create", now);

                created += 1;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    private static string FormatDateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    private static string CreateDeterministicId(string sourceId, string scheduledDate)
    {
        var input = $"{sourceId}:{scheduledDate}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes).ToString("D");
    }

    private sealed record RecurrenceSpec(
        string Type,
        IReadOnlyList<int> Weekdays,
        IReadOnlyList<int> MonthDays
    );

    private sealed record RecurrenceSource(
        string Id,
        string Page,
        string TitleText,
        string TitleJson,
        string ContentJson,
        RecurrenceSpec Recurrence
    );

    private static IReadOnlyList<string> GetOccurrenceDates(
        RecurrenceSpec recurrence,
        DateTime startDate,
        DateTime endDate,
        DateTime weekStart,
        DateTime weekEnd)
    {
        var results = new List<string>();
        if (recurrence.Type == "weekly")
        {
            for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
            {
                var weekday = DayOfWeekToNumber(date.DayOfWeek);
                if (recurrence.Weekdays.Contains(weekday))
                {
                    results.Add(FormatDateKey(date));
                }
            }
            return results;
        }

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (recurrence.Type == "monthly")
            {
                if (recurrence.MonthDays.Contains(date.Day))
                {
                    results.Add(FormatDateKey(date));
                }
            }
        }
        return results;
    }

    private static int DayOfWeekToNumber(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            DayOfWeek.Sunday => 7,
            _ => 1
        };
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

    private static async Task<List<RecurrenceSource>> GetRecurringSourcesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var sources = new List<RecurrenceSource>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, page, title, title_json, content_json, recurrence_json
            FROM tasks
            WHERE recurrence_json IS NOT NULL;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recurrenceJson = reader.GetString(5);
            if (string.IsNullOrWhiteSpace(recurrenceJson))
            {
                continue;
            }
            if (!TryParseRecurrence(recurrenceJson, out var recurrence))
            {
                continue;
            }
            if (recurrence.Type == "repeatable" || recurrence.Type == "notes")
            {
                continue;
            }
            if (recurrence.Type == "weekly" && recurrence.Weekdays.Count == 0)
            {
                continue;
            }
            if (recurrence.Type == "monthly" && recurrence.MonthDays.Count == 0)
            {
                continue;
            }
            sources.Add(new RecurrenceSource(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? ParseTitleJson(null, reader.GetString(2)).GetRawText() : reader.GetString(3),
                reader.GetString(4),
                recurrence));
        }

        return sources;
    }

    private static bool TryParseRecurrence(string recurrenceJson, out RecurrenceSpec recurrence)
    {
        recurrence = new RecurrenceSpec("repeatable", Array.Empty<int>(), Array.Empty<int>());
        try
        {
            using var doc = JsonDocument.Parse(recurrenceJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            var type = typeElement.GetString() ?? string.Empty;
            var weekdays = Array.Empty<int>();
            var monthDays = Array.Empty<int>();

            if (type == "weekly" && root.TryGetProperty("weekdays", out var weekdayElement) && weekdayElement.ValueKind == JsonValueKind.Array)
            {
                weekdays = weekdayElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetInt32())
                    .Where(value => value >= 1 && value <= 7)
                    .Distinct()
                    .ToArray();
            }

            if (type == "monthly" && root.TryGetProperty("monthDays", out var monthElement) && monthElement.ValueKind == JsonValueKind.Array)
            {
                monthDays = monthElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetInt32())
                    .Where(value => value >= 1 && value <= 31)
                    .Distinct()
                    .ToArray();
            }

            recurrence = new RecurrenceSpec(type, weekdays, monthDays);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
