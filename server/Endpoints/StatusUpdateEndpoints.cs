using Glance.Server.StatusUpdates;
using Glance.Server.Integrations.Microsoft;
using Microsoft.Extensions.Options;

namespace Glance.Server;

internal static class StatusUpdateEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/status-updates", async (StatusSummaryService service, CancellationToken token) =>
            Results.Ok(await service.GetOverviewAsync(token)));

        app.MapPost("/api/status-updates/authenticate", async (
            HttpContext context,
            IMicrosoftTokenProvider tokens,
            IOptions<StatusIntegrationOptions> configured,
            CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                var sources = new List<string>();
                if (configured.Value.AzureDevOps.Enabled)
                {
                    await tokens.GetAzureDevOpsTokenAsync(token);
                    sources.Add("Azure DevOps");
                }
                if (configured.Value.Outlook.Enabled)
                {
                    await tokens.GetGraphTokenAsync(token);
                    sources.Add("Outlook");
                }
                return sources.Count == 0
                    ? Results.BadRequest(new { error = "NotConfigured", message = "Enable a workplace status source before signing in." })
                    : Results.Ok(new { ok = true, message = $"Microsoft sign-in is ready for {string.Join(" and ", sources)}." });
            }
            catch (WorkplaceIntegrationNotConfiguredException ex)
            {
                return Results.BadRequest(new { error = "NotConfigured", message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "AuthenticationFailed", message = ex.Message });
            }
        });

        app.MapPost("/api/status-updates/collect", async (HttpContext context, StatusCollectRequest request, StatusSummaryService service, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await service.CollectAsync(request, token));
        });

        app.MapGet("/api/status-updates/json", async (StatusSummaryService service, CancellationToken token) =>
        {
            try
            {
                var result = await service.GetLatestJsonAsync(token);
                return Results.File(result.Json, "application/json", $"StatusSummary_{result.Run.ReportDate}.json");
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = "NotFound", message = ex.Message });
            }
        });

        app.MapGet("/api/status-updates/schema", (AppPaths paths) =>
        {
            var path = Path.Combine(paths.AppRoot, "schema", "status-summary.schema.json");
            return Results.File(path, "application/schema+json", "StatusSummary.schema.json");
        });

        app.MapPost("/api/status-updates/import", async (HttpRequest request, HttpContext context, StatusSummaryService service, CancellationToken token) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "ValidationError", message = "Expected multipart form data" });
            var form = await request.ReadFormAsync(token);
            var file = form.Files.GetFile("summary");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "ValidationError", message = "Select a StatusSummary.json file" });
            if (file.Length > 20L * 1024 * 1024) return Results.BadRequest(new { error = "ValidationError", message = "The JSON file exceeds 20 MB" });
            await using var stream = file.OpenReadStream();
            var result = await service.ImportAsync(stream, token);
            return result.ValidationErrors.Count > 0
                ? Results.BadRequest(new { error = "ValidationError", message = "The completed status file is invalid", errors = result.ValidationErrors })
                : Results.Ok(result);
        }).DisableAntiforgery();

        app.MapGet("/api/status-updates/excel", async (StatusSummaryService service, StatusOfficeExporter exporter, CancellationToken token) =>
        {
            try
            {
                var document = await service.GetExportDocumentAsync(token);
                return Results.File(exporter.CreateExcel(document), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"StatusSummary_{document.ReportDate}.xlsx");
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = "ExportUnavailable", message = ex.Message });
            }
        });

        app.MapGet("/api/status-updates/powerpoint", async (StatusSummaryService service, StatusOfficeExporter exporter, CancellationToken token) =>
        {
            try
            {
                var document = await service.GetExportDocumentAsync(token);
                return Results.File(exporter.CreatePowerPoint(document), "application/vnd.openxmlformats-officedocument.presentationml.presentation", $"StatusSummary_{document.ReportDate}.pptx");
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = "ExportUnavailable", message = ex.Message });
            }
        });
    }
}
