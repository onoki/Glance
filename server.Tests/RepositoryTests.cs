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
    public async Task Recurrence_GeneratesWeeklyTasksForCurrentWeekOnly()
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

        var startDate = new DateTime(2024, 3, 6);
        await app.Tasks.GenerateRecurringTasksAsync(startDate, CancellationToken.None);

        var currentWeekMonday = "2024-03-04";
        var nextWeekMonday = "2024-03-11";
        var currentCount = await CountTasksForDate(app.Paths.ConnectionString, currentWeekMonday);
        var nextCount = await CountTasksForDate(app.Paths.ConnectionString, nextWeekMonday);
        Assert.True(currentCount > 0);
        Assert.Equal(0, nextCount);
    }

    [Fact]
    public async Task Recurrence_AcceptsZeroBasedWeekdays()
    {
        await using var app = TestAppFixture.Create();
        using var recurrenceDoc = JsonDocument.Parse("{\"type\":\"weekly\",\"weekdays\":[0]}");
        var request = new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Weekly"),
            TestAppFixture.CreateContent("Recurring"),
            1,
            null,
            recurrenceDoc.RootElement.Clone());
        await app.Tasks.CreateTaskAsync(request, CancellationToken.None);

        var startDate = new DateTime(2024, 3, 6);
        await app.Tasks.GenerateRecurringTasksAsync(startDate, CancellationToken.None);

        var sunday = "2024-03-10";
        var count = await CountTasksForDate(app.Paths.ConnectionString, sunday);
        Assert.True(count > 0);
    }

    [Fact]
    public async Task History_MoveCompletedToHistory_MovesOnlyTodayTasks()
    {
        await using var app = TestAppFixture.Create();
        var todayTask = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Today"),
            TestAppFixture.CreateContent("Done"),
            1,
            null,
            null), CancellationToken.None);

        var yesterdayTask = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Yesterday"),
            TestAppFixture.CreateContent("Done"),
            2,
            null,
            null), CancellationToken.None);

        var startOfToday = GetStartOfToday();
        var todayCompletedAt = startOfToday + 1000;
        var yesterdayCompletedAt = startOfToday - 1000;

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET completed_at = $completed WHERE id = $id;";
            command.Parameters.AddWithValue("$completed", todayCompletedAt);
            command.Parameters.AddWithValue("$id", todayTask.TaskId);
            await command.ExecuteNonQueryAsync();

            command.Parameters["$completed"].Value = yesterdayCompletedAt;
            command.Parameters["$id"].Value = yesterdayTask.TaskId;
            await command.ExecuteNonQueryAsync();
        }

        var moved = await app.Tasks.MoveCompletedToHistoryAsync(startOfToday, CancellationToken.None);
        Assert.Equal(1, moved);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(startOfToday - 1, await GetCompletedAt(connection, todayTask.TaskId));
            Assert.Equal(yesterdayCompletedAt, await GetCompletedAt(connection, yesterdayTask.TaskId));
        }
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

    [Fact]
    public async Task Dashboard_NewTasksKeepCompletedTodayVisibleUntilTomorrow()
    {
        await using var app = TestAppFixture.Create();
        var completedToday = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardNew,
            TestAppFixture.CreateTitle("Completed today"),
            TestAppFixture.CreateContent("Can still be undone"),
            1,
            null,
            null), CancellationToken.None);
        var completedEarlier = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardNew,
            TestAppFixture.CreateTitle("Completed earlier"),
            TestAppFixture.CreateContent("History only"),
            2,
            null,
            null), CancellationToken.None);
        var startOfToday = GetStartOfToday();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET completed_at = $completed WHERE id = $id;";
            command.Parameters.AddWithValue("$completed", startOfToday + 1000);
            command.Parameters.AddWithValue("$id", completedToday.TaskId);
            await command.ExecuteNonQueryAsync();

            command.Parameters["$completed"].Value = startOfToday - 1000;
            command.Parameters["$id"].Value = completedEarlier.TaskId;
            await command.ExecuteNonQueryAsync();
        }

        var tasks = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, startOfToday, CancellationToken.None);
        Assert.Contains(tasks, task => task.Id == completedToday.TaskId && task.CompletedAt == startOfToday + 1000);
        Assert.DoesNotContain(tasks, task => task.Id == completedEarlier.TaskId);
    }

    [Fact]
    public void StatusMarkers_KeepRaisedTimeAndCanBeRemovedFromCopies()
    {
        const string raised = "2026-07-01T08:00:00.000Z";
        using var titleDoc = JsonDocument.Parse($$"""{"type":"doc","content":[{"type":"paragraph","attrs":{"glanceId":"section-1","statusInputAtUtc":"{{raised}}"},"content":[{"type":"text","text":"Risk raised"}]}]}""");
        var title = titleDoc.RootElement.Clone();
        var content = TestAppFixture.CreateContent("Context");
        var markers = StatusMarkerExtractor.Extract(title, content);

        var marker = Assert.Single(markers);
        Assert.Equal("section-1", marker.SectionId);
        Assert.Equal("Risk raised", marker.Text);
        Assert.Equal(DateTimeOffset.Parse(raised).ToUnixTimeMilliseconds(), marker.RaisedAt);
        Assert.DoesNotContain("statusInputAtUtc", StatusMarkerExtractor.RemoveMarkers(title).GetRawText());
    }

    [Fact]
    public async Task People_SendCopiesByTagAndPreservesSourceWithAuditEvent()
    {
        await using var app = TestAppFixture.Create();
        var person = await app.People.CreatePersonAsync("Alice", CancellationToken.None);
        var tag = await app.People.CreateTagAsync("Team members", CancellationToken.None);
        await app.People.SetPersonTagsAsync(person.Id, new[] { tag.Id }, CancellationToken.None);
        var source = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Discuss launch"),
            TestAppFixture.CreateContent("Ask for date"),
            1, null, null), CancellationToken.None);

        var sent = await app.Tasks.SendToPeopleAsync(source.TaskId, new TaskSendToPeopleRequest(Array.Empty<string>(), new[] { tag.Id }), CancellationToken.None);
        Assert.NotNull(sent);
        Assert.Equal(1, sent.Created);
        var personTasks = await app.Tasks.GetPersonTasksAsync(person.Id, GetStartOfToday(), CancellationToken.None);
        Assert.Equal("From Dashboard", Assert.Single(personTasks).OriginLabel);
        var sendEvent = Assert.Single(await app.Tasks.GetSendEventsAsync(source.TaskId, CancellationToken.None));
        Assert.Equal("tag", sendEvent.DestinationKind);
        Assert.Equal("Team members", sendEvent.DestinationLabel);
        Assert.Contains((await app.Tasks.GetDashboardMainTasksAsync(0, CancellationToken.None)), task => task.Id == source.TaskId && task.SendMarkerVisible);
        await app.Tasks.DismissSendMarkerAsync(source.TaskId, CancellationToken.None);
        Assert.Contains((await app.Tasks.GetDashboardMainTasksAsync(0, CancellationToken.None)), task => task.Id == source.TaskId && !task.SendMarkerVisible);
        await app.Tasks.SendToPeopleAsync(source.TaskId, new TaskSendToPeopleRequest(new[] { person.Id }, Array.Empty<string>()), CancellationToken.None);
        Assert.Contains((await app.Tasks.GetDashboardMainTasksAsync(0, CancellationToken.None)), task => task.Id == source.TaskId && task.SendMarkerVisible);
    }

    [Fact]
    public async Task People_CompletedTodayStaysVisibleUntilTomorrowAndCanBeUndone()
    {
        await using var app = TestAppFixture.Create();
        var person = await app.People.CreatePersonAsync("Alice", CancellationToken.None);
        var openTask = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.PeopleMain, TestAppFixture.CreateTitle("Open"), TestAppFixture.CreateContent("Keep working"),
            1, null, null, person.Id), CancellationToken.None);
        var completedToday = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.PeopleMain, TestAppFixture.CreateTitle("Completed today"), TestAppFixture.CreateContent("Can undo"),
            2, null, null, person.Id), CancellationToken.None);
        var completedEarlier = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.PeopleMain, TestAppFixture.CreateTitle("Completed earlier"), TestAppFixture.CreateContent("History only"),
            3, null, null, person.Id), CancellationToken.None);
        var startOfToday = GetStartOfToday();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(app.Paths.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET completed_at = $completed WHERE id = $id;";
            command.Parameters.AddWithValue("$completed", startOfToday + 1000);
            command.Parameters.AddWithValue("$id", completedToday.TaskId);
            await command.ExecuteNonQueryAsync();

            command.Parameters["$completed"].Value = startOfToday - 1000;
            command.Parameters["$id"].Value = completedEarlier.TaskId;
            await command.ExecuteNonQueryAsync();
        }

        var visibleTasks = await app.Tasks.GetPersonTasksAsync(person.Id, startOfToday, CancellationToken.None);
        Assert.Contains(visibleTasks, task => task.Id == openTask.TaskId && task.CompletedAt is null);
        Assert.Contains(visibleTasks, task => task.Id == completedToday.TaskId && task.CompletedAt == startOfToday + 1000);
        Assert.DoesNotContain(visibleTasks, task => task.Id == completedEarlier.TaskId);
        Assert.Contains(await app.Tasks.GetHistoryTasksAsync(CancellationToken.None), task => task.Id == completedEarlier.TaskId);

        await app.Tasks.SetCompletionAsync(completedToday.TaskId, false, CancellationToken.None);
        var afterUndo = await app.Tasks.GetPersonTasksAsync(person.Id, startOfToday, CancellationToken.None);
        Assert.Contains(afterUndo, task => task.Id == completedToday.TaskId && task.CompletedAt is null);
    }

    [Fact]
    public async Task People_PositionUpdateChangesActiveOrdering()
    {
        await using var app = TestAppFixture.Create();
        var alice = await app.People.CreatePersonAsync("Alice", CancellationToken.None);
        var bob = await app.People.CreatePersonAsync("Bob", CancellationToken.None);
        var carol = await app.People.CreatePersonAsync("Carol", CancellationToken.None);

        var initial = await app.People.GetAsync(false, CancellationToken.None);
        Assert.Equal(new[] { alice.Id, bob.Id, carol.Id }, initial.People.Select(person => person.Id));

        var updated = await app.People.UpdatePersonAsync(
            carol.Id,
            new PersonUpdateRequest(null, 0, null),
            CancellationToken.None);

        Assert.True(updated);
        var reordered = await app.People.GetAsync(false, CancellationToken.None);
        Assert.Equal(new[] { carol.Id, alice.Id, bob.Id }, reordered.People.Select(person => person.Id));
    }

    [Fact]
    public async Task People_ArchiveAndRestorePreservesNotes()
    {
        await using var app = TestAppFixture.Create();
        var person = await app.People.CreatePersonAsync("Bob", CancellationToken.None);
        var note = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.PeopleMain, TestAppFixture.CreateTitle("Question"), TestAppFixture.CreateContent("Follow up"),
            1, null, null, person.Id), CancellationToken.None);

        await app.People.UpdatePersonAsync(person.Id, new PersonUpdateRequest(null, null, true), CancellationToken.None);
        Assert.Empty((await app.People.GetAsync(false, CancellationToken.None)).People);
        Assert.Single(await app.Tasks.GetPersonTasksAsync(person.Id, GetStartOfToday(), CancellationToken.None));
        await app.People.UpdatePersonAsync(person.Id, new PersonUpdateRequest(null, null, false), CancellationToken.None);
        Assert.Single((await app.People.GetAsync(false, CancellationToken.None)).People);
        await app.Tasks.SetCompletionAsync(note.TaskId, true, CancellationToken.None);
        var historyTask = Assert.Single(await app.Tasks.GetHistoryTasksAsync(CancellationToken.None));
        Assert.Equal("Bob", historyTask.OwnerPersonName);
        Assert.Equal(1, (await app.Tasks.GetHistoryStatsAsync(0, CancellationToken.None)).Sum(day => day.Count));
    }

    private static async Task<long?> GetCompletedAt(Microsoft.Data.Sqlite.SqliteConnection connection, string taskId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT completed_at FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        var result = await command.ExecuteScalarAsync();
        return result is long value ? value : null;
    }

    private static long GetStartOfToday()
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return start.ToUnixTimeMilliseconds();
    }

    private static async Task<int> CountTasksForDate(string connectionString, string date)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM tasks
            WHERE scheduled_date = $date;
            """;
        command.Parameters.AddWithValue("$date", date);
        var result = await command.ExecuteScalarAsync();
        return result is long value ? (int)value : 0;
    }
}
