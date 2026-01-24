namespace Glance.Server;

internal static class UpdateEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/update", async (HttpRequest request, HttpContext context, UpdateService updates, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Update package must be sent as multipart form data" });
            }

            var form = await request.ReadFormAsync(token);
            var file = form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "No update package was provided" });
            }

            if (file.Length <= 0)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Update package is empty" });
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".zip")
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Update package must be a .zip file" });
            }

            try
            {
                var version = await updates.ApplyUpdateAsync(file, token);
                return Results.Ok(new { ok = true, version, message = "Update staged. Restarting now..." });
            }
            catch (UpdatePackageException ex)
            {
                return Results.BadRequest(new { error = "ValidationError", message = ex.Message });
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Update failed.");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        });
    }
}
