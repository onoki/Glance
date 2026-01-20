namespace Glance.Server;

internal static class VersionEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/version", () => Results.Ok(new { version = BuildInfo.Version }));
    }
}
