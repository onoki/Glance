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
}
