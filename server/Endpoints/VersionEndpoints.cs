namespace Glance.Server;

internal static class VersionEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/version", () => Results.Ok(new { version = BuildInfo.Version }));
        // Stable across windows/ports, without exposing the local database path.
        app.MapGet("/api/clipboard-scope", (AppPaths paths) => Results.Ok(new {
            scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Path.GetFullPath(paths.DatabasePath).ToUpperInvariant())))
        }));
    }
}
