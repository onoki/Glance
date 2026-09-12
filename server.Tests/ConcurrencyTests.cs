using System.Text.Json;
using Glance.Server;

namespace Glance.Server.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task StaleTaskUpdate_IsRejectedWithoutOverwritingNewerText()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Original"),
            TestAppFixture.CreateContent("Original content"),
            1,
            null,
            null), CancellationToken.None);

        var first = await app.Tasks.UpdateTaskAsync(created.TaskId, new TaskUpdateRequest(
            created.UpdatedAt,
            TestAppFixture.CreateTitle("Window A"),
            null,
            null,
            null,
            null,
            null), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<TaskWriteConflictException>(() =>
            app.Tasks.UpdateTaskAsync(created.TaskId, new TaskUpdateRequest(
                created.UpdatedAt,
                TestAppFixture.CreateTitle("Window B"),
                null,
                null,
                null,
                null,
                null), CancellationToken.None));

        Assert.Equal(first!.UpdatedAt, conflict.CurrentUpdatedAt);
        var stored = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, long.MaxValue, CancellationToken.None);
        Assert.Contains(stored, task => task.Id == created.TaskId &&
            TaskTextExtractor.ExtractPlainText(task.Title).Contains("Window A", StringComparison.Ordinal));
        Assert.DoesNotContain(stored, task => task.Id == created.TaskId &&
            TaskTextExtractor.ExtractPlainText(task.Title).Contains("Window B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleStatusMarkerUpdate_IsRejected()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardMain,
            TestAppFixture.CreateTitle("Original"),
            TestAppFixture.CreateContent("Original content"),
            1,
            null,
            null), CancellationToken.None);
        var first = await app.Tasks.UpdateTaskAsync(created.TaskId, new TaskUpdateRequest(
            created.UpdatedAt,
            TestAppFixture.CreateTitle("Changed elsewhere"),
            null,
            null,
            null,
            null,
            null), CancellationToken.None);

        var title = TestAppFixture.CreateTitle("Marked");
        var content = TestAppFixture.CreateContent("Status input");
        var error = await Assert.ThrowsAsync<TaskWriteConflictException>(() =>
            app.Tasks.SetStatusMarkersAsync(created.TaskId,
                new TaskStatusMarkersRequest(created.UpdatedAt, title, content),
                CancellationToken.None));

        Assert.Equal(first!.UpdatedAt, error.CurrentUpdatedAt);
    }
}
