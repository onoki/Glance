using System.Text.Json;

namespace Glance.Server;

internal static class TaskEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/dashboard", async (TaskRepository tasks, CancellationToken token) =>
        {
            var startOfToday = EndpointHelpers.GetStartOfToday();
            var newTasks = await tasks.GetTasksByPageAsync(TaskPages.DashboardNew, startOfToday, token);
            var mainTasks = await tasks.GetDashboardMainTasksAsync(startOfToday, token);
            return Results.Ok(new DashboardResponse(newTasks, mainTasks));
        });

        app.MapPost("/api/tasks", async (TaskCreateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (request.Title.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task title is required" });
            }

            if (request.Content.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content is required" });
            }

            var validation = TaskValidation.ValidateTaskInput(request.Title, request.Content);
            if (validation != null)
            {
                return validation;
            }

            var scheduleValidation = TaskValidation.ValidateScheduledDate(request.ScheduledDate);
            if (scheduleValidation != null)
            {
                return scheduleValidation;
            }

            var recurrenceValidation = TaskValidation.ValidateRecurrence(request.Recurrence);
            if (recurrenceValidation != null)
            {
                return recurrenceValidation;
            }

            var response = await tasks.CreateTaskAsync(request, token);
            if (request.Recurrence is { ValueKind: JsonValueKind.Object })
            {
                await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            }
            return Results.Ok(response);
        });

        app.MapPut("/api/tasks/{taskId}", async (string taskId, TaskUpdateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (ValidateUpdate(request) is { } validation) return validation;

            TaskUpdateResponse? response;
            try
            {
                response = await tasks.UpdateTaskAsync(taskId, request, token);
            }
            catch (TaskWriteConflictException ex)
            {
                return Results.Conflict(new
                {
                    error = "Conflict",
                    message = ex.Message,
                    currentUpdatedAt = ex.CurrentUpdatedAt
                });
            }
            if (response is null)
            {
                return Results.NotFound(new { error = "NotFound", message = "Task not found" });
            }
            if (request.Recurrence is { ValueKind: JsonValueKind.Object })
            {
                await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            }
            return Results.Ok(response);
        });

        app.MapPut("/api/tasks/batch", async (TaskBatchUpdateItem[] updates, TaskRepository tasks, CancellationToken token) =>
        {
            if (updates.Length == 0 || updates.Any(item => item?.Update is null || string.IsNullOrWhiteSpace(item.TaskId))
                || updates.Select(item => item.TaskId).Distinct().Count() != updates.Length)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Select distinct tasks to update." });
            }
            foreach (var item in updates)
            {
                if (ValidateUpdate(item.Update) is { } validation) return validation;
            }

            IReadOnlyList<TaskUpdateResponse>? responses;
            try
            {
                responses = await tasks.UpdateTasksAsync(updates, token);
            }
            catch (TaskWriteConflictException ex)
            {
                return Results.Conflict(new { error = "Conflict", message = ex.Message, currentUpdatedAt = ex.CurrentUpdatedAt });
            }
            if (responses is null)
            {
                return Results.NotFound(new { error = "NotFound", message = "A selected task no longer exists." });
            }
            if (updates.Any(item => !item.Delete && item.Update.Recurrence is { ValueKind: JsonValueKind.Object }))
            {
                await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            }
            return Results.Ok(responses);
        });

        app.MapPost("/api/tasks/{taskId}/complete", async (string taskId, TaskCompleteRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            var response = await tasks.SetCompletionAsync(taskId, request.Completed, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapDelete("/api/tasks/{taskId}", async (string taskId, long? baseUpdatedAt, TaskRepository tasks, CancellationToken token) =>
        {
            bool deleted;
            try { deleted = await tasks.DeleteTaskAsync(taskId, token, baseUpdatedAt); }
            catch (TaskWriteConflictException ex)
            {
                return Results.Conflict(new { error = "Conflict", message = "This task changed in another window. It was not deleted.", currentUpdatedAt = ex.CurrentUpdatedAt });
            }
            return deleted
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new { error = "NotFound", message = "Task not found" });
        });

        app.MapPost("/api/tasks/{taskId}/restore", async (string taskId, TaskRepository tasks, CancellationToken token) =>
        {
            var restored = await tasks.RestoreTaskAsync(taskId, token);
            return restored is null
                ? Results.NotFound(new { error = "NotFound", message = "Deleted task not found" })
                : Results.Ok(restored);
        });

        app.MapPost("/api/recurrence/run", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/recurrence/reset", async (HttpContext context, TaskRepository tasks, AppMetaRepository appMeta, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var today = TimeProvider.Now.Date.ToString("yyyy-MM-dd");
            await appMeta.SetValueAsync("recurrence_generated_until", today, token);
            var created = await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            return Results.Ok(new { ok = true, created });
        });
    }

    private static IResult? ValidateUpdate(TaskUpdateRequest request)
    {
        if (request.CompletedAt is { } completed && completed.ValueKind != JsonValueKind.Null
            && (completed.ValueKind != JsonValueKind.Number || !completed.TryGetInt64(out var timestamp) || timestamp <= 0))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Completion time must be a positive integer or null." });
        }
        if (request.GroupId is { } group && group.ValueKind != System.Text.Json.JsonValueKind.Null
            && (group.ValueKind != System.Text.Json.JsonValueKind.String || !group.TryGetGuid(out _)))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task group must be a UUID or null." });
        }

        if (request.Title.HasValue)
        {
            var validation = TaskValidation.ValidateTaskInput(request.Title.Value, request.Content);
            if (validation != null) return validation;
        }
        else if (request.Content.HasValue && TaskTextExtractor.ContainsHeading(request.Content.Value))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
        }

        return TaskValidation.ValidateScheduledDate(request.ScheduledDate)
            ?? TaskValidation.ValidateRecurrence(request.Recurrence);
    }
}
