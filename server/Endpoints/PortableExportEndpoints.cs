using Glance.Server.Portability;

namespace Glance.Server;

internal static class PortableExportEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/export/portable", async (
            HttpContext context,
            PortableExportService exporter,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var result = await exporter.CreateAsync(token);
                return Results.File(result.Content, "application/zip", result.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Portable export is incomplete",
                    detail: ex.Message);
            }
        });
    }
}
