namespace Glance.Server;

internal static class DebugEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/debug/move-completed-to-history", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
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
