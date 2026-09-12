using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glance.Server;

public sealed record StatusMarkedSection(
    string SectionId,
    string Text,
    long RaisedAt
);

public static class StatusMarkerExtractor
{
    public static IReadOnlyList<StatusMarkedSection> Extract(JsonElement title, JsonElement content)
    {
        var results = new List<StatusMarkedSection>();
        ExtractFrom(title, results);
        ExtractFrom(content, results);
        return results
            .GroupBy(item => item.SectionId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.RaisedAt).First())
            .OrderBy(item => item.RaisedAt)
            .ToList();
    }

    public static long? GetEarliestTimestamp(JsonElement title, JsonElement content)
    {
        var markers = Extract(title, content);
        return markers.Count == 0 ? null : markers.Min(item => item.RaisedAt);
    }

    public static JsonElement RemoveMarkers(JsonElement source)
    {
        var node = JsonNode.Parse(source.GetRawText());
        RemoveMarkers(node);
        using var document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static void RemoveMarkers(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["attrs"] is JsonObject attrs)
            {
                attrs.Remove("statusInputAtUtc");
            }
            foreach (var value in obj.ToList())
            {
                RemoveMarkers(value.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                RemoveMarkers(child);
            }
        }
    }

    private static void ExtractFrom(JsonElement element, List<StatusMarkedSection> results)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryReadMarker(element, out var section))
            {
                results.Add(section);
            }

            if (element.TryGetProperty("content", out var content))
            {
                ExtractFrom(content, results);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in element.EnumerateArray())
        {
            ExtractFrom(child, results);
        }
    }

    private static bool TryReadMarker(JsonElement element, out StatusMarkedSection section)
    {
        section = default!;
        if (!element.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (!attrs.TryGetProperty("statusInputAtUtc", out var timestampElement) || timestampElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        if (!DateTimeOffset.TryParse(timestampElement.GetString(), out var timestamp))
        {
            return false;
        }

        var sectionId = attrs.TryGetProperty("glanceId", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            return false;
        }

        var textSource = element;
        if (element.TryGetProperty("type", out var type) && type.GetString() == "listItem" &&
            element.TryGetProperty("content", out var nodeContent) && nodeContent.ValueKind == JsonValueKind.Array &&
            nodeContent.GetArrayLength() > 0 && nodeContent[0].ValueKind == JsonValueKind.Object)
        {
            textSource = nodeContent[0];
        }

        section = new StatusMarkedSection(
            sectionId,
            TaskTextExtractor.ExtractPlainText(textSource),
            timestamp.ToUniversalTime().ToUnixTimeMilliseconds());
        return true;
    }
}
