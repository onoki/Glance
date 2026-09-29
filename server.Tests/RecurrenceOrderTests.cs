using System.Text.Json;
namespace Glance.Server.Tests;
public class RecurrenceOrderTests
{
    [Theory]
    [InlineData("weekly")]
    [InlineData("monthly")]
    public async Task GeneratedTasksPrecedeExistingDayTasksWithoutReorderingOnRerun(string type)
    {
        await using var app = TestAppFixture.Create();
        var date = new DateTime(2026, 9, 28);
        var scheduled = JsonSerializer.SerializeToElement("2026-09-28");
        foreach (var position in new[] { -25.5, 0, 99 })
            await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardMain, TestAppFixture.CreateTitle("Existing"), TestAppFixture.CreateContent("Keep me"), position, scheduled, null), default);
        var rule = type == "weekly" ? "{\"type\":\"weekly\",\"weekdays\":[1]}" : "{\"type\":\"monthly\",\"monthDays\":[28]}";
        using var recurrence = JsonDocument.Parse(rule);
        await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardMain, TestAppFixture.CreateTitle("Recurring"), TestAppFixture.CreateContent("Details"), 100, null, recurrence.RootElement.Clone()), default);
        Assert.True(await app.Tasks.GenerateRecurringTasksAsync(date, default) > 0);
        var rows = (await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, long.MaxValue, default)).Where(row => row.ScheduledDate == "2026-09-28").OrderBy(row => row.Position).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal("Recurring", TaskTextExtractor.ExtractPlainText(rows[0].Title));
        Assert.True(rows[0].Position < -25.5);
        var before = rows.Select(row => (row.Id, row.Position)).ToArray();
        Assert.Equal(0, await app.Tasks.GenerateRecurringTasksAsync(date, default));
        rows = (await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, long.MaxValue, default)).Where(row => row.ScheduledDate == "2026-09-28").OrderBy(row => row.Position).ToList();
        Assert.Equal(before, rows.Select(row => (row.Id, row.Position)).ToArray());
    }
}
