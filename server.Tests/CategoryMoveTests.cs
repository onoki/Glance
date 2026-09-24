using System.Text.Json;
namespace Glance.Server.Tests;

public class CategoryMoveTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task EveryCategoryTransition_CanSetAndClearScheduleAndRecurrence()
    {
        await using var app = TestAppFixture.Create();
        var categories = new (string Page, string? Date, object? Recurrence)[] {
            (TaskPages.DashboardNew, null, null),
            (TaskPages.DashboardMain, null, null),
            (TaskPages.DashboardMain, "2026-09-24", null),
            (TaskPages.DashboardMain, "2026-09-28", null),
            (TaskPages.DashboardMain, "no-date", null),
            (TaskPages.DashboardMain, null, new { type = "notes" }),
            (TaskPages.DashboardMain, null, new { type = "weekly", weekdays = new[] { 1 } })
        };
        foreach (var source in categories)
        foreach (var destination in categories)
        {
            var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(source.Page,
                TestAppFixture.CreateTitle("Keep title"), TestAppFixture.CreateContent("Keep children"), 1,
                JsonSerializer.SerializeToElement(source.Date), JsonSerializer.SerializeToElement(source.Recurrence)), default);
            var payload = JsonSerializer.Serialize(new { baseUpdatedAt = created.UpdatedAt, page = destination.Page,
                scheduledDate = destination.Date, recurrence = destination.Recurrence });
            // Exercise the HTTP JSON deserialization boundary, including literal null.
            var request = JsonSerializer.Deserialize<TaskUpdateRequest>(payload, Json)!;
            Assert.True(request.ScheduledDate.HasValue);
            Assert.True(request.Recurrence.HasValue);
            await app.Tasks.UpdateTaskAsync(created.TaskId, request, default);
            var rows = await app.Tasks.GetTasksByPageAsync(destination.Page, long.MaxValue, default);
            var moved = Assert.Single(rows, row => row.Id == created.TaskId);
            Assert.Equal(destination.Date, moved.ScheduledDate);
            Assert.Equal(JsonSerializer.Serialize(destination.Recurrence), moved.Recurrence?.GetRawText() ?? "null");
            Assert.Equal("Keep title", TaskTextExtractor.ExtractPlainText(moved.Title));
            Assert.Contains("Keep children", TaskTextExtractor.ExtractPlainText(moved.Content));
            // An ordinary text edit omits both fields and must preserve the category.
            var textOnly = JsonSerializer.Deserialize<TaskUpdateRequest>(JsonSerializer.Serialize(new {
                baseUpdatedAt = moved.UpdatedAt, title = TestAppFixture.CreateTitle("Edited") }), Json)!;
            Assert.False(textOnly.ScheduledDate.HasValue);
            Assert.False(textOnly.Recurrence.HasValue);
            await app.Tasks.UpdateTaskAsync(created.TaskId, textOnly, default);
            rows = await app.Tasks.GetTasksByPageAsync(destination.Page, long.MaxValue, default);
            moved = Assert.Single(rows, row => row.Id == created.TaskId);
            Assert.Equal(destination.Date, moved.ScheduledDate);
            Assert.Equal(JsonSerializer.Serialize(destination.Recurrence), moved.Recurrence?.GetRawText() ?? "null");
        }
    }
}
