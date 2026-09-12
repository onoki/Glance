namespace Glance.Server;

internal static class DataSafetyEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/backups", async (DataSafetyService dataSafety, CancellationToken token) =>
        {
            var backups = await dataSafety.GetCatalogAsync(token);
            return Results.Ok(new { backups });
        });

        app.MapPost("/api/backups", async (HttpContext context, DataSafetyService dataSafety, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var result = await dataSafety.CreateBackupAsync("manual", TimeProvider.Now, token);
            return result.Success
                ? Results.Ok(new
                {
                    ok = true,
                    backupId = result.BackupId,
                    mirrored = result.Mirrored,
                    mirrorError = result.MirrorError
                })
                : Results.Json(new { error = "BackupError", message = result.Error }, statusCode: StatusCodes.Status500InternalServerError);
        });

        app.MapPost("/api/backups/{backupId}/verify", async (
            string backupId,
            HttpContext context,
            DataSafetyService dataSafety,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            try
            {
                var result = await dataSafety.VerifyBackupAsync(backupId, token);
                return result.IsValid
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = "NotFound", message = ex.Message });
            }
        });

        app.MapGet("/api/data-safety/settings", async (DataSafetyService dataSafety, CancellationToken token) =>
            Results.Ok(await dataSafety.GetSettingsAsync(token)));

        app.MapPut("/api/data-safety/settings", async (
            HttpContext context,
            DataSafetySettings request,
            DataSafetyService dataSafety,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            try
            {
                return Results.Ok(await dataSafety.SaveSettingsAsync(request, token));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "ValidationError", message = ex.Message });
            }
        });

        app.MapPost("/api/data-safety/test-location", async (
            HttpContext context,
            DataSafetyLocationTestRequest request,
            DataSafetyService dataSafety,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            return Results.Ok(await dataSafety.TestLocationAsync(request.Location, token));
        });

        app.MapPost("/api/data-safety/restore", async (
            HttpContext context,
            BackupRestoreRequest request,
            DataSafetyService dataSafety,
            DataSafetyStartupState startupState,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            try
            {
                var response = await dataSafety.StageRestoreAsync(request.BackupId, startupState.Info.Healthy, token);
                return Results.Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = "NotFound", message = ex.Message });
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = "RestoreError", message = ex.Message });
            }
        });

        app.MapGet("/api/data-safety/startup", (DataSafetyStartupState startupState, AppPaths paths) =>
            Results.Ok(startupState.Info with { PendingRestore = File.Exists(paths.PendingRestorePath) }));
    }
}
