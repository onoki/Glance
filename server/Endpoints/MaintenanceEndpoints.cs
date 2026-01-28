namespace Glance.Server;

internal static class MaintenanceEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/backup", async (HttpContext context, MaintenanceService maintenance, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var now = TimeProvider.Now;
            var success = await maintenance.CreateBackupAsync(now, token);
            if (success)
            {
                await maintenance.RecordBackupSuccessAsync(now);
                return Results.Ok(new { ok = true });
            }
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        });

        app.MapPost("/api/search/reindex", async (HttpContext context, MaintenanceService maintenance, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            await maintenance.ReindexSearchAsync(token);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/maintenance/daily", (HttpContext context, MaintenanceService maintenance, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            _ = Task.Run(() => maintenance.RunDailyMaintenanceAsync(TimeProvider.Now, CancellationToken.None), token);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/warnings", async (MaintenanceService maintenance, CancellationToken token) =>
        {
            var warnings = await maintenance.GetWarningsAsync(token);
            return Results.Ok(new WarningsResponse(warnings));
        });

        app.MapGet("/api/maintenance/status", async (MaintenanceService maintenance, AppMetaRepository appMeta, CancellationToken token) =>
        {
            var status = await maintenance.GetStatusAsync();
            var recurrenceGeneratedUntil = await appMeta.GetValueAsync("recurrence_generated_until", token);
            return Results.Ok(new
            {
                status.LastBackupAt,
                status.LastBackupError,
                status.LastReindexAt,
                RecurrenceGeneratedUntil = recurrenceGeneratedUntil
            });
        });
    }
}
