using System.Net;
using Microsoft.AspNetCore.Http;

namespace Glance.Server;

internal static class EndpointHelpers
{
    internal static bool IsLocalRequest(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address != null && IPAddress.IsLoopback(address);
    }

    internal static long GetStartOfToday()
    {
        return GetDayStart(DateTimeOffset.Now, TimeZoneInfo.Local, 0);
    }

    internal static long GetHistoryStart(int days)
    {
        return GetDayStart(DateTimeOffset.Now, TimeZoneInfo.Local, -days + 1);
    }

    internal static long GetDayStart(DateTimeOffset now, TimeZoneInfo zone, int offsetDays)
    {
        var date = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(offsetDays);
        return new DateTimeOffset(date, zone.GetUtcOffset(date)).ToUnixTimeMilliseconds();
    }

    internal static string FormatDate(long? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "Unknown";
        }
        var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp.Value).ToLocalTime().Date;
        return date.ToString("yyyy-MM-dd");
    }

    internal static bool IsAllowedContentType(string contentType)
    {
        return contentType is "image/png"
            or "image/jpeg"
            or "image/webp"
            or "image/gif";
    }

    internal static string? GetAllowedExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => extension,
            ".jpg" => extension,
            ".jpeg" => extension,
            ".webp" => extension,
            ".gif" => extension,
            _ => null
        };
    }

    internal static string? GetExtensionFromContentType(string contentType)
    {
        return contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => null
        };
    }
}
