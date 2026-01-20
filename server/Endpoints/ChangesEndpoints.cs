namespace Glance.Server;

internal static class ChangesEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/changes", async (long? since, ChangeLogRepository changes, CancellationToken token) =>
        {
            var response = await changes.GetChangesAsync(since ?? 0, token);
            return Results.Ok(response);
        });
    }
}
