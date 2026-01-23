using Glance.Server;
using Microsoft.AspNetCore.Builder;
using PhotinoNET;

namespace Glance.Desktop;

internal static class Program
{
    private static string? _logPath;

    [STAThread]
    private static void Main()
    {
        var appRoot = ResolveAppRoot();
        EnsureDirectories(appRoot);
        InitLogging(appRoot);
        Log($"App root: {appRoot}");

        var port = ResolvePort();
        Log($"Server port: {port}");
        WebApplication server;
        try
        {
            server = ServerHost.StartAsync(appRoot, port, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Log($"Server startup failed: {ex}");
            return;
        }

        var devServer = Environment.GetEnvironmentVariable("GLANCE_DEV_SERVER_URL");
        var forceDevServer = ParseBool(Environment.GetEnvironmentVariable("GLANCE_USE_DEV_SERVER"));
        var distIndex = Path.Combine(appRoot, "ui", "dist", "index.html");
        var useDevServer = forceDevServer || (string.IsNullOrWhiteSpace(devServer) && !File.Exists(distIndex));
        var startUrl = useDevServer
            ? string.IsNullOrWhiteSpace(devServer) ? "http://localhost:5173/" : devServer
            : $"http://127.0.0.1:{port}/";
        var iconPath = ResolveIconPath();
        Log($"Dev server URL: {devServer ?? "<empty>"}");
        Log($"Force dev server: {forceDevServer}");
        Log($"Dist index exists: {File.Exists(distIndex)}");
        Log($"Start URL: {startUrl}");

        var window = new PhotinoWindow()
            .SetTitle("Glance")
            .SetUseOsDefaultSize(false)
            .SetSize(1200, 800)
            .SetMinSize(1200, 800)
            .SetResizable(true)
            .Center();

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            window.SetIconFile(iconPath);
        }

        window.Load(startUrl);
        window.WaitForClose();
        Log("Window closed, stopping server.");

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

    private static void InitLogging(string appRoot)
    {
        _logPath = Path.Combine(appRoot, "data", "desktop.log");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"Unhandled exception: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };
    }

    private static void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }
        try
        {
            File.AppendAllText(_logPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static string ResolveIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var icoPath = Path.Combine(baseDir, "icon.ico");
        if (OperatingSystem.IsWindows() && File.Exists(icoPath))
        {
            return icoPath;
        }

        return Path.Combine(baseDir, "icon.png");
    }
}
