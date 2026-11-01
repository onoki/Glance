namespace Glance.Server;

internal static class HistoryMaintenanceEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/history/archive-previous-days", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(new { moved = await tasks.ArchivePreviousCompletionsAsync(EndpointHelpers.GetStartOfToday(), token) });
        });
        app.MapPost("/api/history/move-completed-to-history", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var moved = await tasks.MoveCompletedToHistoryAsync(EndpointHelpers.GetStartOfToday(), token);
            return Results.Ok(new { moved });
        });
    }
}
