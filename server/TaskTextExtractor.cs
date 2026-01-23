using System.Text;
using System.Text.Json;

namespace Glance.Server;

public static class TaskTextExtractor
{
    public static string ExtractPlainText(JsonElement content)
    {
        var builder = new StringBuilder();
        AppendText(content, builder);
        return builder.ToString().Trim();
    }

    public static bool ContainsHeading(JsonElement content)
    {
        return HasHeading(content);
    }

    public static bool ContainsList(JsonElement content)
    {
        return HasList(content);
    }

    private static void AppendText(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("text", out var text))
                {
                    builder.Append(text.GetString());
                    builder.Append(' ');
                }

                if (element.TryGetProperty("content", out var content))
                {
                    AppendText(content, builder);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    AppendText(child, builder);
                }
                break;
        }
    }

    private static bool HasHeading(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var typeProperty) &&
                string.Equals(typeProperty.GetString(), "heading", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (element.TryGetProperty("content", out var content))
            {
                return HasHeading(content);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (HasHeading(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var typeProperty))
            {
                var type = typeProperty.GetString();
                if (string.Equals(type, "bulletList", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "orderedList", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "taskList", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (element.TryGetProperty("content", out var content))
            {
                return HasList(content);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (HasList(child))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
