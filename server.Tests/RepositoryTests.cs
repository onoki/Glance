using System.Text.Json;
using Glance.Server;
using Xunit;

namespace Glance.Server.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public void BuildInfoVersion_HasExpectedFormat()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", BuildInfo.Version);
    }

    [Fact]
    public async Task AppMeta_RoundTripsValues()
    {
        await using var app = TestAppFixture.Create();
        await app.Meta.SetValueAsync("app_version", "test-version", CancellationToken.None);
        var value = await app.Meta.GetValueAsync("app_version", CancellationToken.None);
        Assert.Equal("test-version", value);
    }

    [Fact]
    public async Task AppMeta_ReportsSchemaVersion()
    {
        await using var app = TestAppFixture.Create();
        var version = await app.Meta.GetSchemaVersionAsync(CancellationToken.None);
        Assert.True(version >= 2);
    }

    [Fact]
    public async Task Search_FindsPartialWordMatches()
    {
        await using var app = TestAppFixture.Create();
        var title = TestAppFixture.CreateTitle("New task");
        var content = TestAppFixture.CreateContent("Some note");
        var request = new TaskCreateRequest(
            TaskPages.DashboardMain,
            title,
            content,
            1,
            null,
            null);
        var created = await app.Tasks.CreateTaskAsync(request, CancellationToken.None);

        var results = await app.Tasks.SearchAsync("ew", CancellationToken.None);
        Assert.Contains(results, task => task.Id == created.TaskId);
    }

    [Fact]
    public async Task Changes_LogCreateEvents()
    {
        await using var app = TestAppFixture.Create();
        var request = new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Track me"),
            TestAppFixture.CreateContent("Log"),
            1,
            null,
            null);
        await app.Tasks.CreateTaskAsync(request, CancellationToken.None);

        var changes = await app.Changes.GetChangesAsync(0, CancellationToken.None);
        Assert.Contains(changes.Changes, change => change.ChangeType == "create");
    }

    [Fact]
    public async Task Recurrence_GeneratesWeeklyTasks()
    {
        await using var app = TestAppFixture.Create();
        using var recurrenceDoc = JsonDocument.Parse("{\"type\":\"weekly\",\"weekdays\":[1]}");
        var request = new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Weekly"),
            TestAppFixture.CreateContent("Recurring"),
            1,
            null,
            recurrenceDoc.RootElement.Clone());
        await app.Tasks.CreateTaskAsync(request, CancellationToken.None);

        var created = await app.Tasks.GenerateRecurringTasksAsync(DateTime.Today, CancellationToken.None);
        Assert.True(created > 0);
    }

    [Fact]
    public async Task Dashboard_HidesCompletedTasksFromPreviousDays()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Completed"),
            TestAppFixture.CreateContent("Done"),
            1,
            null,
            null), CancellationToken.None);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            var startOfToday = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
            var yesterday = startOfToday - 1000;
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET completed_at = $completed WHERE id = $id;";
            command.Parameters.AddWithValue("$completed", yesterday);
            command.Parameters.AddWithValue("$id", created.TaskId);
            await command.ExecuteNonQueryAsync();
        }

        var tasks = await app.Tasks.GetDashboardMainTasksAsync(GetStartOfToday(), CancellationToken.None);
        Assert.DoesNotContain(tasks, task => task.Id == created.TaskId);
    }

    private static long GetStartOfToday()
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return start.ToUnixTimeMilliseconds();
    }
}
