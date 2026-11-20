using System.Text.Json;
using Glance.Server;

namespace Glance.Server.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task SelectedTaskActionsAreAtomicAndRetainCompletionTimes()
    {
        await using var app = TestAppFixture.Create();
        var first = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew, TestAppFixture.CreateTitle("First"),
            TestAppFixture.CreateContent("Keep first"), 1, null, null), default);
        var second = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew, TestAppFixture.CreateTitle("Second"),
            TestAppFixture.CreateContent("Keep second"), 2, null, null), default);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updates = new[]
        {
            new TaskBatchUpdateItem(first.TaskId, new(first.UpdatedAt, null, null, null, null, null, null,
                IsPrivate: true, CompletedAt: JsonSerializer.SerializeToElement(timestamp))),
            new TaskBatchUpdateItem(second.TaskId, new(second.UpdatedAt - 1, null, null, null, null, null, null), Delete: true)
        };
        await Assert.ThrowsAsync<TaskWriteConflictException>(() => app.Tasks.UpdateTasksAsync(updates, default));
        var original = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default);
        Assert.Equal(2, original.Count);
        Assert.All(original, task => { Assert.Null(task.CompletedAt); Assert.False(task.IsPrivate); });

        updates[1] = updates[1] with { Update = updates[1].Update with { BaseUpdatedAt = second.UpdatedAt } };
        var responses = await app.Tasks.UpdateTasksAsync(updates, default);
        Assert.Equal(2, responses!.Count);
        var retained = Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default));
        Assert.Equal(timestamp, retained.CompletedAt);
        Assert.True(retained.IsPrivate);
        var edited = await app.Tasks.UpdateTaskAsync(first.TaskId,
            new(retained.UpdatedAt, TestAppFixture.CreateTitle("Edited"), null, null, null, null, null), default);
        retained = Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default));
        Assert.Equal(timestamp, retained.CompletedAt);
        var clear = JsonSerializer.Deserialize<TaskUpdateRequest>(
            $$"""{"BaseUpdatedAt":{{edited!.UpdatedAt}},"CompletedAt":null}""")!;
        await app.Tasks.UpdateTaskAsync(first.TaskId, clear, default);
        retained = Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default));
        Assert.Null(retained.CompletedAt);
        Assert.True(retained.IsPrivate);
        Assert.NotNull(await app.Tasks.RestoreTaskAsync(second.TaskId, default));
    }

    [Fact]
    public async Task GroupMoveRollsBackAllRowsOnConflict()
    {
        await using var app = TestAppFixture.Create();
        var first = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew,
            TestAppFixture.CreateTitle("First"), TestAppFixture.CreateContent("Private content"),
            1, null, null, IsPrivate: true), default);
        var second = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew,
            TestAppFixture.CreateTitle("Second"), TestAppFixture.CreateContent("Keep"),
            2, null, null), default);
        var groupId = Guid.NewGuid();
        var group = JsonSerializer.SerializeToElement(groupId);
        var edits = new[]
        {
            new TaskBatchUpdateItem(first.TaskId, new(first.UpdatedAt, null, null, TaskPages.DashboardMain, 10, null, null, GroupId: group)),
            new TaskBatchUpdateItem(second.TaskId, new(second.UpdatedAt - 1, null, null, TaskPages.DashboardMain, 11, null, null, GroupId: group))
        };
        await Assert.ThrowsAsync<TaskWriteConflictException>(() => app.Tasks.UpdateTasksAsync(edits, default));
        Assert.Empty(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default));
        var rolledBack = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default);
        Assert.Equal(2, rolledBack.Count);
        Assert.All(rolledBack, task => Assert.Null(task.GroupId));

        edits[1] = edits[1] with { Update = edits[1].Update with { BaseUpdatedAt = second.UpdatedAt } };
        Assert.Equal(2, (await app.Tasks.UpdateTasksAsync(edits, default))!.Count);
        var moved = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default);
        Assert.Equal(new[] { first.TaskId, second.TaskId }, moved.Select(task => task.Id));
        Assert.True(moved[0].IsPrivate);
        Assert.Contains("Private content", moved[0].Content.GetRawText());
        Assert.All(moved, task => Assert.Equal(groupId.ToString("D"), task.GroupId));
        var saved = await app.Tasks.UpdateTaskAsync(first.TaskId, new(moved[0].UpdatedAt,
            TestAppFixture.CreateTitle("Edited"), null, null, null, null, null), default);
        var afterEdit = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default);
        Assert.Equal(groupId.ToString("D"), afterEdit[0].GroupId);

        var detach = JsonSerializer.Deserialize<TaskUpdateRequest>(
            $$"""{"BaseUpdatedAt":{{saved!.UpdatedAt}},"GroupId":null}""")!;
        await app.Tasks.UpdateTaskAsync(first.TaskId, detach, default);
        var afterDetach = await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardMain, 0, default);
        Assert.Null(afterDetach[0].GroupId);
        Assert.Equal(groupId.ToString("D"), afterDetach[1].GroupId);
    }

    [Fact]
    public async Task CreateTaskCanPersistAnAlreadyCheckedSubtask()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new(TaskPages.DashboardNew,
            TestAppFixture.CreateTitle("Promoted"), TestAppFixture.CreateContent("Children"),
            1, null, null, Completed: true), default);
        var row = Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default));
        Assert.NotNull(created.CompletedAt);
        Assert.Equal(created.CompletedAt, row.CompletedAt);
        Assert.Equal("Promoted", TaskTextExtractor.ExtractPlainText(row.Title));
    }

    [Fact]
    public async Task StaleClipboardCut_PreservesNewerText()
    {
        await using var app = TestAppFixture.Create();
        var created = await app.Tasks.CreateTaskAsync(new TaskCreateRequest(
            TaskPages.DashboardNew, TestAppFixture.CreateTitle("Clipboard source"),
            TestAppFixture.CreateContent("Keep all nested notes"), 1, null, null), default);
        var edited = await app.Tasks.UpdateTaskAsync(created.TaskId, new TaskUpdateRequest(
            created.UpdatedAt, TestAppFixture.CreateTitle("Newer text"), null, null, null, null, null), default);
        await Assert.ThrowsAsync<TaskWriteConflictException>(() => app.Tasks.DeleteTaskAsync(created.TaskId, default, created.UpdatedAt));
        Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, long.MaxValue, default));
        Assert.True(await app.Tasks.DeleteTaskAsync(created.TaskId, default, edited!.UpdatedAt));
        Assert.Empty(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, long.MaxValue, default));
        Assert.NotNull(await app.Tasks.RestoreTaskAsync(created.TaskId, default));
    }

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
}
