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
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return start.ToUnixTimeMilliseconds();
    }

    internal static long GetHistoryStart(int days)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).AddDays(-days + 1);
        return start.ToUnixTimeMilliseconds();
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
