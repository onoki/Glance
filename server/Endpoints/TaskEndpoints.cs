using System.Text.Json;

namespace Glance.Server;

internal static class TaskEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/dashboard", async (TaskRepository tasks, CancellationToken token) =>
        {
            var newTasks = await tasks.GetTasksByPageAsync(TaskPages.DashboardNew, token);
            var mainTasks = await tasks.GetDashboardMainTasksAsync(EndpointHelpers.GetStartOfToday(), token);
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
            if (request.Recurrence.HasValue)
            {
                await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            }
            return Results.Ok(response);
        });

        app.MapPut("/api/tasks/{taskId}", async (string taskId, TaskUpdateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (request.Title.HasValue)
            {
                var validation = TaskValidation.ValidateTaskInput(request.Title.Value, request.Content);
                if (validation != null)
                {
                    return validation;
                }
            }
            else if (request.Content.HasValue && TaskTextExtractor.ContainsHeading(request.Content.Value))
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
            }

            var scheduleValidation = TaskValidation.ValidateScheduledDate(request.ScheduledDate.HasValue ? request.ScheduledDate.Value : null);
            if (scheduleValidation != null)
            {
                return scheduleValidation;
            }

            var recurrenceValidation = TaskValidation.ValidateRecurrence(request.Recurrence);
            if (recurrenceValidation != null)
            {
                return recurrenceValidation;
            }

            var response = await tasks.UpdateTaskAsync(taskId, request, token);
            if (response is null)
            {
                return Results.NotFound(new { error = "NotFound", message = "Task not found" });
            }
            if (request.Recurrence.HasValue)
            {
                await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, token);
            }
            return Results.Ok(response);
        });

        app.MapPost("/api/tasks/{taskId}/complete", async (string taskId, TaskCompleteRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            var response = await tasks.SetCompletionAsync(taskId, request.Completed, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapDelete("/api/tasks/{taskId}", async (string taskId, TaskRepository tasks, CancellationToken token) =>
        {
            var deleted = await tasks.DeleteTaskAsync(taskId, token);
            return deleted
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new { error = "NotFound", message = "Task not found" });
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
}
