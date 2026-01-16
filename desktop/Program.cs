using Glance.Server;
using PhotinoNET;

namespace Glance.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var appRoot = ResolveAppRoot();
        EnsureDirectories(appRoot);

        var port = ResolvePort();
        var server = ServerHost.StartAsync(appRoot, port, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var devServer = Environment.GetEnvironmentVariable("GLANCE_DEV_SERVER_URL");
        var forceDevServer = ParseBool(Environment.GetEnvironmentVariable("GLANCE_USE_DEV_SERVER"));
        var distIndex = Path.Combine(appRoot, "ui", "dist", "index.html");
        var useDevServer = forceDevServer || (string.IsNullOrWhiteSpace(devServer) && !File.Exists(distIndex));
        var startUrl = useDevServer
            ? string.IsNullOrWhiteSpace(devServer) ? "http://localhost:5173/" : devServer
            : $"http://127.0.0.1:{port}/";

        var window = new PhotinoWindow()
            .SetTitle("Glance")
            .SetUseOsDefaultSize(false)
            .SetSize(800, 600)
            .SetMinSize(800, 600)
            .SetResizable(true)
            .Center();

        window.Load(startUrl);
        window.WaitForClose();

        server.StopAsync().GetAwaiter().GetResult();
    }

    private static string ResolveAppRoot()
    {
        var env = Environment.GetEnvironmentVariable("GLANCE_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return AppRootLocator.Find(AppContext.BaseDirectory);
    }

    private static int ResolvePort()
    {
        var env = Environment.GetEnvironmentVariable("GLANCE_PORT");
        return int.TryParse(env, out var port) ? port : 5588;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectories(string appRoot)
    {
        Directory.CreateDirectory(Path.Combine(appRoot, "data"));
        Directory.CreateDirectory(Path.Combine(appRoot, "blobs"));
        Directory.CreateDirectory(Path.Combine(appRoot, "blobs", "attachments"));
    }
}
