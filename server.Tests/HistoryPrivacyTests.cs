using Glance.Server;
using Microsoft.Data.Sqlite;

namespace Glance.Server.Tests;

public sealed class HistoryPrivacyTests
{
    [Fact]
    public async Task Migration006AddsDefaultsWithoutChangingExistingDocumentsOrDates()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew, TestAppFixture.CreateTitle("Legacy KEEP"),
            TestAppFixture.CreateContent("Legacy body KEEP"), 1, null, null), default);
        await app.Tasks.SetCompletionAsync(created.TaskId, true, default);
        var before = Assert.Single(await app.Tasks.GetHistoryTasksAsync(default));
        await using (var connection = new SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE tasks DROP COLUMN is_private; ALTER TABLE tasks DROP COLUMN history_archived_at; INSERT OR IGNORE INTO schema_migrations VALUES(5, 0); DELETE FROM schema_migrations WHERE version = 6;";
            await command.ExecuteNonQueryAsync();
        }
        new DatabaseInitializer(app.Paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance).Initialize();
        Assert.Equal(6, await app.Meta.GetSchemaVersionAsync(default));
        var after = Assert.Single(await app.Tasks.GetHistoryTasksAsync(default));
        Assert.Equal(before.Title.GetRawText(), after.Title.GetRawText());
        Assert.Equal(before.Content.GetRawText(), after.Content.GetRawText());
        Assert.Equal(before.CompletedAt, after.CompletedAt);
        Assert.False(after.IsPrivate); Assert.Null(after.HistoryArchivedAt);
    }

    [Fact]
    public async Task ArchivingPreservesCompletionDaysAndPrivateDataAndIsIdempotent()
    {
        await using var app = TestAppFixture.Create();
        var person = await app.People.CreatePersonAsync("Person", default);
        var start = EndpointHelpers.GetStartOfToday();
        var ids = new List<string>();
        foreach (var page in new[] { TaskPages.DashboardNew, TaskPages.DashboardMain, TaskPages.PeopleMain })
        {
            foreach (var completed in new long?[] { start - 1000, start + 1000, null })
            {
                var created = await app.Tasks.CreateTaskAsync(new(page, TestAppFixture.CreateTitle("Public title"),
                    TestAppFixture.CreateContent("Private KEEP"), ids.Count + 1, null, null,
                    page == TaskPages.PeopleMain ? person.Id : null, IsPrivate: true), default);
                ids.Add(created.TaskId);
                await using var connection = new SqliteConnection(app.Paths.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE tasks SET completed_at = $completed WHERE id = $id";
                command.Parameters.AddWithValue("$id", created.TaskId);
                command.Parameters.AddWithValue("$completed", completed is null ? DBNull.Value : completed.Value);
                await command.ExecuteNonQueryAsync();
            }
        }
        var before = await app.Tasks.GetHistoryStatsAsync(start - 86400000, default);
        Assert.Equal(3, await app.Tasks.ArchivePreviousCompletionsAsync(start, default));
        Assert.Equal(0, await app.Tasks.ArchivePreviousCompletionsAsync(start, default));
        Assert.Equal(3, await app.Tasks.MoveCompletedToHistoryAsync(start, default));
        Assert.Equal(0, await app.Tasks.MoveCompletedToHistoryAsync(start, default));
        Assert.Equal(before, await app.Tasks.GetHistoryStatsAsync(start - 86400000, default));
        var history = await app.Tasks.GetHistoryTasksAsync(default);
        Assert.Equal(6, history.Count);
        Assert.All(history, task => {
            Assert.True(task.IsPrivate);
            Assert.NotNull(task.HistoryArchivedAt);
            Assert.Contains("Private KEEP", task.Content.GetRawText());
            Assert.Contains(task.CompletedAt, new long?[] { start - 1000, start + 1000 });
        });
        Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, start, default));
        Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, start, default));
        Assert.Single(await app.Tasks.GetPersonTasksAsync(person.Id, start, default));
        var archived = history.First(task => task.OwnerPersonId == person.Id);
        var recheck = await app.Tasks.SetCompletionAsync(archived.Id, true, default);
        Assert.Equal(archived.CompletedAt, recheck!.CompletedAt);
        Assert.Single(await app.Tasks.GetPersonTasksAsync(person.Id, start, default));
        await app.Tasks.SetCompletionAsync(archived.Id, false, default);
        var restored = Assert.Single(await app.Tasks.GetPersonTasksAsync(person.Id, start, default), task => task.Id == archived.Id);
        Assert.Null(restored.HistoryArchivedAt);
        Assert.True(restored.IsPrivate);
        Assert.Equal(5, (await app.Tasks.GetHistoryStatsAsync(start - 86400000, default)).Sum(day => day.Count));
    }

    [Theory]
    [InlineData("2026-03-29T15:00:00+03:00", "2026-03-29T00:00:00+02:00")]
    [InlineData("2026-10-25T15:00:00+02:00", "2026-10-25T00:00:00+03:00")]
    public void DayBoundaryUsesOffsetAtMidnight(string current, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
        Assert.Equal(DateTimeOffset.Parse(expected).ToUnixTimeMilliseconds(),
            EndpointHelpers.GetDayStart(DateTimeOffset.Parse(current), zone, 0));
        var historical = TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(current), zone).Date.AddDays(-179);
        Assert.Equal(new DateTimeOffset(historical, zone.GetUtcOffset(historical)).ToUnixTimeMilliseconds(),
            EndpointHelpers.GetDayStart(DateTimeOffset.Parse(current), zone, -179));
    }

    [Fact]
    public async Task PrivacySurvivesPartialUpdatesDeleteRestoreAndRecurringGeneration()
    {
        await using var app = TestAppFixture.Create();
        using var recurrence = System.Text.Json.JsonDocument.Parse("""{"type":"weekly","weekdays":[1]}""");
        var created = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardMain, TestAppFixture.CreateTitle("Private recurrence"),
            TestAppFixture.CreateContent("KEEP"), 1, null, recurrence.RootElement.Clone(), IsPrivate: true), default);
        var update = await app.Tasks.UpdateTaskAsync(created.TaskId, new(created.UpdatedAt, TestAppFixture.CreateTitle("Changed"), null, null, null, null, null), default);
        Assert.NotNull(update);
        Assert.True(Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default)).IsPrivate);
        Assert.True(await app.Tasks.GenerateRecurringTasksAsync(new DateTime(2026, 10, 26), default) > 0);
        Assert.All(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default), task => Assert.True(task.IsPrivate));
        await app.Tasks.DeleteTaskAsync(created.TaskId, default);
        await app.Tasks.RestoreTaskAsync(created.TaskId, default);
        Assert.True(Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default), task => task.Id == created.TaskId).IsPrivate);
    }
}
