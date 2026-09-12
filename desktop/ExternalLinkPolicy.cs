using System.Diagnostics;

namespace Glance.Desktop;

internal static class ExternalLinkPolicy
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        Uri.UriSchemeFile
    };

    private static readonly HashSet<string> BlockedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appref-ms", ".bat", ".cmd", ".com", ".cpl", ".exe", ".hta", ".inf", ".ins", ".isp",
        ".js", ".jse", ".lnk", ".msh", ".msh1", ".msh2", ".mshxml", ".msi", ".msp", ".mst",
        ".pif", ".ps1", ".ps1xml", ".ps2", ".ps2xml", ".psc1", ".psc2", ".reg", ".scf", ".scr",
        ".sct", ".url", ".vb", ".vbe", ".vbs", ".ws", ".wsc", ".wsf", ".wsh"
    };

    internal static bool TryResolve(string? input, out string target, out string error)
    {
        target = string.Empty;
        error = "The link is invalid.";
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsControl))
        {
            return false;
        }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !AllowedSchemes.Contains(uri.Scheme))
        {
            error = "Only http(s), mail, mapped-drive, UNC, and file links are allowed.";
            return false;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }
            target = uri.AbsoluteUri;
            return true;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(uri.OriginalString["mailto:".Length..]))
            {
                return false;
            }
            target = uri.OriginalString;
            return true;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "File links cannot contain a query string or fragment.";
            return false;
        }

        string path;
        try
        {
            path = uri.LocalPath;
        }
        catch (Exception)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            error = "The file link must use an absolute drive or network path.";
            return false;
        }
        if (BlockedFileExtensions.Contains(Path.GetExtension(path)))
        {
            error = "Glance will not launch executable, script, shortcut, or installer files.";
            return false;
        }

        target = path;
        return true;
    }

    internal static void Open(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
            ErrorDialog = false
        });
    }
}
