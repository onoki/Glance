using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Glance.Server;

internal static class TaskValidation
{
    internal static IResult? ValidateTaskInput(JsonElement title, JsonElement? content)
    {
        if (title.ValueKind == JsonValueKind.Undefined)
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title is required" });
        }

        if (TaskTextExtractor.ContainsHeading(title))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must not contain heading nodes" });
        }

        if (TaskTextExtractor.ContainsList(title))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must not contain list nodes" });
        }

        if (content.HasValue && TaskTextExtractor.ContainsHeading(content.Value))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
        }

        return null;
    }

    internal static IResult? ValidateScheduledDate(JsonElement? scheduledDate)
    {
        if (!scheduledDate.HasValue)
        {
            return null;
        }

        return scheduledDate.Value.ValueKind switch
        {
            JsonValueKind.String => null,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => Results.BadRequest(new { error = "ValidationError", message = "Scheduled date must be a string or null" })
        };
    }

    internal static IResult? ValidateRecurrence(JsonElement? recurrence)
    {
        if (!recurrence.HasValue)
        {
            return null;
        }

        return recurrence.Value.ValueKind switch
        {
            JsonValueKind.Object => null,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => Results.BadRequest(new { error = "ValidationError", message = "Recurrence must be an object or null" })
        };
    }
}
