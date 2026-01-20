using System.Linq;
using Microsoft.AspNetCore.StaticFiles;

namespace Glance.Server;

internal static class AttachmentEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/attachments", async (HttpRequest request, HttpContext context, AppPaths paths) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment must be sent as multipart form data" });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "No attachment was provided" });
            }

            const long maxBytes = 10 * 1024 * 1024;
            if (file.Length <= 0)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment is empty" });
            }

            if (file.Length > maxBytes)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment exceeds the 10MB size limit" });
            }

            var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(contentType) && !EndpointHelpers.IsAllowedContentType(contentType))
            {
                return Results.BadRequest(new { error = "ValidationError", message = $"Unsupported attachment type: {contentType}" });
            }

            var extension = EndpointHelpers.GetAllowedExtension(file.FileName)
                ?? EndpointHelpers.GetExtensionFromContentType(contentType)
                ?? ".png";
            var attachmentId = Guid.NewGuid().ToString();
            var fileName = $"{attachmentId}{extension}";

            Directory.CreateDirectory(paths.AttachmentsDirectory);
            var filePath = Path.Combine(paths.AttachmentsDirectory, fileName);
            await using (var stream = File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Ok(new { attachmentId, url = $"{baseUrl}/attachments/{fileName}" });
        });

        app.MapGet("/attachments/{fileName}", (string fileName, HttpContext context, AppPaths paths) =>
        {
            if (!EndpointHelpers.IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return Results.NotFound();
            }

            var filePath = Path.Combine(paths.AttachmentsDirectory, safeName);
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.File(filePath, contentType);
        });
    }
}
