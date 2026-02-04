using System.Drawing;
using Glance.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PhotinoNET;

namespace Glance.Desktop;

internal static class Program
{
    private static string? _logPath;
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;
    private const int MinWidth = 100;
    private const int MinHeight = 100;
    private const string WindowSizeKey = "window_size";

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
            : $"http://127.0.0.1:{port}/?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var iconPath = ResolveIconPath();
        Log($"Dev server URL: {devServer ?? "<empty>"}");
        Log($"Force dev server: {forceDevServer}");
        Log($"Dist index exists: {File.Exists(distIndex)}");
        Log($"Start URL: {startUrl}");

        var appMeta = server.Services.GetRequiredService<AppMetaRepository>();
        var savedSize = LoadWindowSize(appMeta);
        var initialSize = ClampSize(savedSize ?? new Size(DefaultWidth, DefaultHeight));
        var lastNormalSize = initialSize;
        var isMaximized = false;

        var window = new PhotinoWindow()
            .SetTitle("Glance")
            .SetUseOsDefaultSize(false)
            .SetSize(initialSize.Width, initialSize.Height)
            .SetMinSize(MinWidth, MinHeight)
            .SetResizable(true)
            .Center();

        var pendingSize = window.Size;
        var sizeSaveTimer = new System.Threading.Timer(_ =>
        {
            SaveWindowSize(appMeta, pendingSize);
        }, null, Timeout.Infinite, Timeout.Infinite);

        window.WindowSizeChanged += (_, _) =>
        {
            var currentSize = window.Size;
            var maximized = window.Maximized;
            if (!maximized)
            {
                isMaximized = false;
                lastNormalSize = currentSize;
                pendingSize = currentSize;
                sizeSaveTimer.Change(500, Timeout.Infinite);
            }
            else
            {
                isMaximized = true;
            }
        };
        window.WindowMaximized += (_, _) =>
        {
            isMaximized = true;
        };
        window.WindowRestored += (_, _) =>
        {
            isMaximized = false;
            var currentSize = window.Size;
            lastNormalSize = currentSize;
            pendingSize = currentSize;
            sizeSaveTimer.Change(500, Timeout.Infinite);
        };
        window.WindowClosing += (_, _) =>
        {
            var currentSize = window.Size;
            var maximized = isMaximized || window.Maximized;
            SaveWindowSize(appMeta, maximized ? lastNormalSize : currentSize);
            return false;
        };

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            window.SetIconFile(iconPath);
        }

        window.Load(startUrl);
        window.WaitForClose();
        sizeSaveTimer.Dispose();
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

    private static Size ClampSize(Size size)
    {
        var width = Math.Max(MinWidth, size.Width);
        var height = Math.Max(MinHeight, size.Height);
        return new Size(width, height);
    }

    private static Size? LoadWindowSize(AppMetaRepository meta)
    {
        try
        {
            var value = meta.GetValueAsync(WindowSizeKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return TryParseWindowSize(value, out var size) ? size : null;
        }
        catch (Exception ex)
        {
            Log($"Failed to load window size: {ex}");
            return null;
        }
    }

    private static void SaveWindowSize(AppMetaRepository meta, Size size)
    {
        try
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }
            meta.SetValueAsync(WindowSizeKey, $"{size.Width}x{size.Height}", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Log($"Failed to save window size: {ex}");
        }
    }

    private static bool TryParseWindowSize(string? value, out Size size)
    {
        size = new Size(DefaultWidth, DefaultHeight);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var parts = value.Split('x');
        if (parts.Length != 2)
        {
            return false;
        }
        if (!int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
        {
            return false;
        }
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        size = new Size(width, height);
        return true;
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
