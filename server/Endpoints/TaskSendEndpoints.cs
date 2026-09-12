namespace Glance.Server;

internal static class TaskSendEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPut("/api/tasks/{taskId}/status-markers", async (
            string taskId,
            TaskStatusMarkersRequest request,
            TaskRepository tasks,
            CancellationToken token) =>
        {
            var validation = TaskValidation.ValidateTaskInput(request.Title, request.Content);
            if (validation is not null)
            {
                return validation;
            }
            TaskStatusMarkersResponse? response;
            try
            {
                response = await tasks.SetStatusMarkersAsync(taskId, request, token);
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
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapPost("/api/tasks/{taskId}/send-to-people", async (
            string taskId,
            TaskSendToPeopleRequest request,
            TaskRepository tasks,
            CancellationToken token) =>
        {
            var response = await tasks.SendToPeopleAsync(taskId, request, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapPost("/api/tasks/{taskId}/send-to-dashboard", async (string taskId, TaskRepository tasks, CancellationToken token) =>
        {
            var response = await tasks.SendToDashboardAsync(taskId, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapGet("/api/tasks/{taskId}/send-events", async (string taskId, TaskRepository tasks, CancellationToken token) =>
            Results.Ok(new { events = await tasks.GetSendEventsAsync(taskId, token) }));

        app.MapPost("/api/tasks/{taskId}/send-marker/dismiss", async (string taskId, TaskRepository tasks, CancellationToken token) =>
            await tasks.DismissSendMarkerAsync(taskId, token)
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new { error = "NotFound", message = "Task not found" }));
    }
}
