using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Glance.Server.Integrations.Microsoft;
using Glance.Server.StatusUpdates;
using Microsoft.Extensions.Options;

namespace Glance.Server.Integrations.AzureDevOps;

/// <summary>
/// Saved-WIQL-query Azure DevOps REST starting point.
/// TODO(WORKPLACE_INTEGRATION): verify organization policy, fields and identity shapes at work.
/// </summary>
public sealed class AzureDevOpsStatusReader : IAzureDevOpsStatusReader
{
    private static readonly string[] StandardFields =
    {
        "System.Id", "System.Title", "System.WorkItemType", "System.State",
        "System.AssignedTo", "System.CreatedDate", "System.ChangedDate", "System.Tags"
    };

    private readonly HttpClient _httpClient;
    private readonly IMicrosoftTokenProvider _tokens;
    private readonly StatusIntegrationOptions _options;

    public AzureDevOpsStatusReader(HttpClient httpClient, IMicrosoftTokenProvider tokens, IOptions<StatusIntegrationOptions> options)
    {
        _httpClient = httpClient;
        _tokens = tokens;
        _options = options.Value;
    }

    public StatusProviderConfiguration GetConfiguration()
    {
        var configured = _options.Microsoft.Enabled && _options.AzureDevOps.Enabled &&
            !string.IsNullOrWhiteSpace(_options.AzureDevOps.Organization) &&
            !string.IsNullOrWhiteSpace(_options.AzureDevOps.Project) &&
            !string.IsNullOrWhiteSpace(_options.AzureDevOps.SavedQueryId);
        return new StatusProviderConfiguration(configured,
            configured ? "Configured; workplace validation is still required." : "Workplace integration is not configured.");
    }

    public async Task<StatusSourceResult<AzureStatusData>> ReadAsync(StatusPeriod period, CancellationToken token)
    {
        var config = GetConfiguration();
        var settings = _options.AzureDevOps;
        var empty = new AzureStatusData(settings.Organization.NullIfEmpty(), settings.Project.NullIfEmpty(), settings.SavedQueryId.NullIfEmpty(), Array.Empty<AzureWorkItemInput>());
        if (!config.IsConfigured)
        {
            return new StatusSourceResult<AzureStatusData>("notConfigured", null, config.Message, empty);
        }

        try
        {
            var accessToken = await _tokens.GetAzureDevOpsTokenAsync(token);
            var baseUrl = $"https://dev.azure.com/{Uri.EscapeDataString(settings.Organization)}/{Uri.EscapeDataString(settings.Project)}";
            var queryUrl = $"{baseUrl}/_apis/wit/wiql/{Uri.EscapeDataString(settings.SavedQueryId)}?api-version=7.1";
            using var queryRequest = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            queryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var queryResponse = await _httpClient.SendAsync(queryRequest, token);
            queryResponse.EnsureSuccessStatusCode();
            using var queryDocument = JsonDocument.Parse(await queryResponse.Content.ReadAsStringAsync(token));
            var ids = ReadIds(queryDocument.RootElement);

            var fields = StandardFields.Concat(settings.AdditionalFields ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray();
            var workItems = new List<AzureWorkItemInput>();
            foreach (var batch in ids.Chunk(200))
            {
                var batchUrl = $"{baseUrl}/_apis/wit/workitemsbatch?api-version=7.1";
                using var batchRequest = new HttpRequestMessage(HttpMethod.Post, batchUrl);
                batchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                batchRequest.Content = JsonContent.Create(new { ids = batch, fields, errorPolicy = "Omit" });
                using var batchResponse = await _httpClient.SendAsync(batchRequest, token);
                batchResponse.EnsureSuccessStatusCode();
                using var batchDocument = JsonDocument.Parse(await batchResponse.Content.ReadAsStringAsync(token));
                if (!batchDocument.RootElement.TryGetProperty("value", out var values))
                {
                    continue;
                }
                foreach (var value in values.EnumerateArray())
                {
                    workItems.Add(MapWorkItem(value, settings.AdditionalFields ?? new List<string>(), period.FromUtc));
                }
            }

            var data = new AzureStatusData(settings.Organization, settings.Project, settings.SavedQueryId, workItems);
            return new StatusSourceResult<AzureStatusData>("ok", DateTimeOffset.UtcNow, null, data);
        }
        catch (Exception ex)
        {
            return new StatusSourceResult<AzureStatusData>("error", DateTimeOffset.UtcNow, ex.Message, empty);
        }
    }

    private static int[] ReadIds(JsonElement root)
    {
        var ids = new HashSet<int>();
        if (root.TryGetProperty("workItems", out var workItems))
        {
            foreach (var item in workItems.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id)) ids.Add(id.GetInt32());
            }
        }
        if (root.TryGetProperty("workItemRelations", out var relations))
        {
            foreach (var relation in relations.EnumerateArray())
            {
                foreach (var side in new[] { "source", "target" })
                {
                    if (relation.TryGetProperty(side, out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id))
                    {
                        ids.Add(id.GetInt32());
                    }
                }
            }
        }
        return ids.ToArray();
    }

    private static AzureWorkItemInput MapWorkItem(JsonElement item, IReadOnlyList<string> additionalFieldNames, DateTimeOffset since)
    {
        var fields = item.GetProperty("fields");
        var created = ReadDate(fields, "System.CreatedDate");
        var changed = ReadDate(fields, "System.ChangedDate");
        var additional = new Dictionary<string, object?>();
        foreach (var name in additionalFieldNames ?? Array.Empty<string>())
        {
            if (fields.TryGetProperty(name, out var value)) additional[name] = ReadScalar(value);
        }
        return new AzureWorkItemInput(
            item.GetProperty("id").GetInt32(),
            item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            ReadString(fields, "System.WorkItemType") ?? string.Empty,
            ReadString(fields, "System.Title") ?? string.Empty,
            ReadString(fields, "System.State") ?? string.Empty,
            ReadIdentity(fields, "System.AssignedTo"),
            created,
            changed,
            (created ?? changed) >= since || changed >= since,
            (ReadString(fields, "System.Tags") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            additional);
    }

    private static string? ReadString(JsonElement fields, string name) => fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? ReadDate(JsonElement fields, string name) => DateTimeOffset.TryParse(ReadString(fields, name), out var value) ? value.ToUniversalTime() : null;
    private static string? ReadIdentity(JsonElement fields, string name) => fields.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.TryGetProperty("displayName", out var display) ? display.GetString() : null
        : null;
    private static object? ReadScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}

internal static class StringIntegrationExtensions
{
    internal static string? NullIfEmpty(this string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
