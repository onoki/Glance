using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Glance.Server.Integrations.Microsoft;
using Glance.Server.StatusUpdates;
using Microsoft.Extensions.Options;

namespace Glance.Server.Integrations.Outlook;

/// <summary>
/// Microsoft Graph selected-folder reader starting point.
/// TODO(WORKPLACE_INTEGRATION): validate folder selection, consent and body handling at work.
/// </summary>
public sealed partial class OutlookStatusReader : IOutlookStatusReader
{
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftTokenProvider _tokens;
    private readonly StatusIntegrationOptions _options;

    public OutlookStatusReader(HttpClient httpClient, IMicrosoftTokenProvider tokens, IOptions<StatusIntegrationOptions> options)
    {
        _httpClient = httpClient;
        _tokens = tokens;
        _options = options.Value;
    }

    public StatusProviderConfiguration GetConfiguration()
    {
        var configured = _options.Microsoft.Enabled && _options.Outlook.Enabled && !string.IsNullOrWhiteSpace(_options.Outlook.FolderId);
        return new StatusProviderConfiguration(configured,
            configured ? "Configured; workplace validation is still required." : "Workplace integration is not configured.");
    }

    public async Task<StatusSourceResult<OutlookStatusData>> ReadAsync(StatusPeriod period, CancellationToken token)
    {
        var settings = _options.Outlook;
        var folder = string.IsNullOrWhiteSpace(settings.FolderId)
            ? null
            : new OutlookFolderInput(settings.FolderId, string.IsNullOrWhiteSpace(settings.FolderDisplayName) ? "Selected folder" : settings.FolderDisplayName);
        var empty = new OutlookStatusData(folder, Array.Empty<OutlookMessageInput>());
        var config = GetConfiguration();
        if (!config.IsConfigured)
        {
            return new StatusSourceResult<OutlookStatusData>("notConfigured", null, config.Message, empty);
        }

        try
        {
            var accessToken = await _tokens.GetGraphTokenAsync(token);
            var filterDate = period.ContextFromUtc.UtcDateTime.ToString("O");
            var select = "id,conversationId,subject,from,receivedDateTime,body,webLink";
            var nextUrl = $"https://graph.microsoft.com/v1.0/me/mailFolders/{Uri.EscapeDataString(settings.FolderId)}/messages" +
                $"?$select={Uri.EscapeDataString(select)}&$filter=receivedDateTime%20ge%20{Uri.EscapeDataString(filterDate)}&$orderby=receivedDateTime%20desc&$top=50";
            var messages = new List<OutlookMessageInput>();
            while (!string.IsNullOrWhiteSpace(nextUrl) && messages.Count < Math.Max(1, settings.MaximumMessages))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\", IdType=\"ImmutableId\"");
                using var response = await _httpClient.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
                {
                    if (messages.Count >= settings.MaximumMessages) break;
                    messages.Add(MapMessage(item, period.FromUtc, Math.Max(1000, settings.MaximumBodyCharacters)));
                }
                nextUrl = document.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            }

            return new StatusSourceResult<OutlookStatusData>("ok", DateTimeOffset.UtcNow, null, new OutlookStatusData(folder, messages));
        }
        catch (Exception ex)
        {
            return new StatusSourceResult<OutlookStatusData>("error", DateTimeOffset.UtcNow, ex.Message, empty);
        }
    }

    private static OutlookMessageInput MapMessage(JsonElement item, DateTimeOffset since, int maximumBodyCharacters)
    {
        var received = DateTimeOffset.TryParse(item.GetProperty("receivedDateTime").GetString(), out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.MinValue;
        var rawBody = item.TryGetProperty("body", out var body) && body.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty
            : string.Empty;
        var plainBody = SanitizeBody(rawBody);
        var truncated = plainBody.Length > maximumBodyCharacters;
        if (truncated) plainBody = plainBody[..maximumBodyCharacters];
        string? sender = null;
        if (item.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var address))
        {
            var name = address.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var email = address.TryGetProperty("address", out var emailValue) ? emailValue.GetString() : null;
            sender = string.IsNullOrWhiteSpace(name) ? email : string.IsNullOrWhiteSpace(email) ? name : $"{name} <{email}>";
        }
        return new OutlookMessageInput(
            item.GetProperty("id").GetString() ?? string.Empty,
            item.TryGetProperty("conversationId", out var conversation) ? conversation.GetString() : null,
            item.TryGetProperty("subject", out var subject) ? subject.GetString() ?? string.Empty : string.Empty,
            sender,
            received,
            plainBody,
            item.TryGetProperty("webLink", out var link) ? link.GetString() : null,
            received >= since,
            truncated);
    }

    private static string SanitizeBody(string value)
    {
        var withoutTags = HtmlTagRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
