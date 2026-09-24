using System.Drawing;
using System.Diagnostics;
using System.Text.Json;
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
    private const string WindowPlacementKey = "window_placement";
    private static AppMetaRepository? _appMeta;
    private static readonly object WindowGate = new();
    private static readonly Dictionary<Guid, WindowState> Windows = new();
    private static PhotinoWindow? _mainWindow;
    private static string? _startUrl;
    private static string? _iconPath;
    private static bool _restartAfterClose;
    private static CloseOperation? _closeOperation;
    private static FlushOperation? _flushOperation;

    [STAThread]
    private static void Main()
    {
        // Grayscale antialiasing avoids RGB fringes without changing Windows settings.
        // Set before any WebView2 environment is created, including child windows.
        if (OperatingSystem.IsWindows())
        {
            const string key = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
            var arguments = Environment.GetEnvironmentVariable(key) ?? "";
            if (!arguments.Contains("--disable-lcd-text", StringComparison.Ordinal))
                Environment.SetEnvironmentVariable(key, $"{arguments} --disable-lcd-text".Trim());
        }
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

#if DEBUG
        var devServer = Environment.GetEnvironmentVariable("GLANCE_DEV_SERVER_URL");
        var startUrl = string.IsNullOrWhiteSpace(devServer) ? "http://localhost:5173/" : devServer;
#else
        var startUrl = $"http://127.0.0.1:{port}/?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
#endif
        var iconPath = ResolveIconPath();
        _startUrl = startUrl;
        _iconPath = iconPath;
        Log($"Start URL: {startUrl}");

        var appMeta = server.Services.GetRequiredService<AppMetaRepository>();
        _appMeta = appMeta;
        var savedSize = LoadWindowSize(appMeta);
        var savedPlacement = LoadWindowPlacement(appMeta) ?? (savedSize is { } legacy
            ? new SavedWindowPlacement(0, 0, legacy.Width, legacy.Height, null) : null);
        var initialPlacement = WindowPlacement.Resolve(savedPlacement, WindowPlacement.GetMonitors());
        var initialSize = new Size(initialPlacement.Width, initialPlacement.Height);

        var window = new PhotinoWindow()
            .SetTitle("Glance")
            .SetUseOsDefaultSize(false)
            .SetSize(initialSize.Width, initialSize.Height)
            .SetMinSize(MinWidth, MinHeight)
            .SetResizable(true)
            .SetUseOsDefaultLocation(false)
            .SetLocation(new Point(initialPlacement.X, initialPlacement.Y));

        _mainWindow = window;
        lock (WindowGate)
        {
            Windows[window.Id] = new WindowState(window, isPrimary: true);
        }

        window.RegisterWebMessageReceivedHandler(HandleWebMessage);
        RegisterZoom(window);
        window.WindowCreated += (_, _) => RestoreWindowPlacement(window, savedPlacement);

        window.WindowClosing += (_, _) => RequestWindowClose(window);

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            window.SetIconFile(iconPath);
        }

        window.Load(startUrl);
        window.WaitForClose();
        Log("Window closed, stopping server.");

        server.StopAsync().GetAwaiter().GetResult();
        if (_restartAfterClose)
        {
            RestartApplication(appRoot);
        }
    }

    private static string ResolveAppRoot()
    {
#if DEBUG
        var env = Environment.GetEnvironmentVariable("GLANCE_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return AppRootLocator.Find(AppContext.BaseDirectory);
#else
        return AppContext.BaseDirectory;
#endif
    }

    private static int ResolvePort()
    {
        var env = Environment.GetEnvironmentVariable("GLANCE_PORT");
        return int.TryParse(env, out var port) ? port : 5588;
    }

    private static SavedWindowPlacement? LoadWindowPlacement(AppMetaRepository meta)
    {
        try
        {
            var json = meta.GetValueAsync(WindowPlacementKey, CancellationToken.None).GetAwaiter().GetResult();
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<SavedWindowPlacement>(json);
        }
        catch (Exception ex) { Log($"Failed to load window placement: {ex}"); return null; }
    }

    private static void SaveWindowPlacement(PhotinoWindow window)
    {
        try
        {
            var placement = OperatingSystem.IsWindows() ? WindowPlacement.Capture(window.WindowHandle) : null;
            if (placement == null || _appMeta == null)
            {
                Log($"Window placement capture unavailable for {window.Id}; existing placement retained.");
                return;
            }
            _appMeta.SetValueAsync(WindowPlacementKey, JsonSerializer.Serialize(placement), CancellationToken.None).GetAwaiter().GetResult();
            SaveWindowSize(_appMeta, new Size(placement.Width, placement.Height));
            Log($"Saved window placement for {window.Id}: {JsonSerializer.Serialize(placement)}");
        }
        catch (Exception ex) { Log($"Failed to save window placement: {ex}"); }
    }

    private static void RestoreWindowPlacement(PhotinoWindow window, SavedWindowPlacement? saved)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // Photino 2.x processes startup coordinates before the native window
            // exists. Reapply after creation, using the final DPI context and
            // signed desktop coordinates (left/above-primary monitors included).
            var monitors = WindowPlacement.GetMonitors();
            var requested = WindowPlacement.Resolve(saved, monitors);
            var before = WindowPlacement.Capture(window.WindowHandle);
            WindowPlacement.Apply(window.WindowHandle, requested);
            var actual = WindowPlacement.Capture(window.WindowHandle);
            Log($"Window placement restore: saved={JsonSerializer.Serialize(saved)}, monitors={JsonSerializer.Serialize(monitors)}, before={JsonSerializer.Serialize(before)}, requested={JsonSerializer.Serialize(requested)}, actual={JsonSerializer.Serialize(actual)}");
        }
        catch (Exception ex) { Log($"Failed to restore native window placement: {ex}"); }
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

    private static void RegisterZoom(PhotinoWindow window)
    {
        window.SetZoom(100);
        window.WindowLocationChanged += (_, _) => SyncZoom(window);
        window.WindowSizeChanged += (_, _) => SyncZoom(window);
    }

    private static void SyncZoom(PhotinoWindow window, bool force = false, int? requested = null)
    {
        if (!Windows.TryGetValue(window.Id, out var state) || (!state.ZoomReady && !force)) return;
        try
        {
            var (monitor, scale) = MonitorZoom.Identify(window.WindowHandle);
            var changed = state.ZoomMonitor != monitor;
            state.ZoomReady = true;
            if (changed || requested.HasValue)
            {
                state.ZoomMonitor = monitor; // Set before native zoom can raise resize events.
                state.ZoomPercent = Math.Clamp(requested ?? MonitorZoom.Get(monitor), 50, 400);
                window.SetZoom(state.ZoomPercent);
                if (requested.HasValue) MonitorZoom.Save(monitor, state.ZoomPercent);
            }
            if (force || changed || state.DisplayScale != scale)
            {
                state.DisplayScale = scale;
                TrySend(window, new { type = "zoomState", percent = state.ZoomPercent, scale });
            }
        }
        catch (Exception ex) { Log($"Unable to update monitor zoom: {ex.Message}"); }
    }

    private static void HandleWebMessage(object? sender, string rawMessage)
    {
        if (sender is not PhotinoWindow window || string.IsNullOrWhiteSpace(rawMessage) || rawMessage.Length > 65536)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            var type = GetString(root, "type");
            var requestId = GetString(root, "requestId");
            switch (type)
            {
                case "openExternal" when !string.IsNullOrWhiteSpace(requestId):
                    HandleOpenExternal(window, requestId, GetString(root, "target"));
                    break;
                case "zoomSync":
                    SyncZoom(window, force: true);
                    break;
                case "setZoom" when root.TryGetProperty("percent", out var percent) && percent.TryGetInt32(out var zoom):
                    SyncZoom(window, force: true, requested: Math.Clamp(zoom, 50, 400));
                    break;
                case "newWindow":
                    CreateChildWindow();
                    break;
                case "pickFolder" when !string.IsNullOrWhiteSpace(requestId):
                    PickFolder(window, requestId, GetString(root, "currentPath"));
                    break;
                case "coordinateFlush" when !string.IsNullOrWhiteSpace(requestId):
                    BeginFlush(window, requestId, GetString(root, "reason") ?? "desktop-action");
                    break;
                case "flushResult" when !string.IsNullOrWhiteSpace(requestId):
                    HandleFlushResult(window, requestId, GetBoolean(root, "ok"), GetString(root, "message"));
                    break;
                case "closeReady" when !string.IsNullOrWhiteSpace(requestId):
                    HandleCloseResult(window, requestId, true, null);
                    break;
                case "closeBlocked" when !string.IsNullOrWhiteSpace(requestId):
                    HandleCloseResult(window, requestId, false, GetString(root, "message"));
                    break;
                case "restartForRestore" when !string.IsNullOrWhiteSpace(requestId):
                    var restartStarted = BeginClose(window, restart: true);
                    SendResult(
                        window,
                        "restartForRestoreResult",
                        requestId,
                        restartStarted,
                        restartStarted ? null : "Glance could not start the coordinated restart because another save or close operation is already in progress.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Invalid desktop message was ignored: {ex.Message}");
        }
    }

    private static void HandleOpenExternal(PhotinoWindow window, string requestId, string? input)
    {
        try
        {
            if (!ExternalLinkPolicy.TryResolve(input, out var target, out var error))
            {
                SendResult(window, "openExternalResult", requestId, false, error);
                return;
            }
            ExternalLinkPolicy.Open(target);
            SendResult(window, "openExternalResult", requestId, true, null);
        }
        catch (Exception ex)
        {
            Log($"Failed to open external link: {ex.Message}");
            SendResult(
                window,
                "openExternalResult",
                requestId,
                false,
                "Windows could not open the link. The file may be unavailable or no application may be registered for it.");
        }
    }

    private static void CreateChildWindow()
    {
        var parent = _mainWindow;
        if (parent is null || string.IsNullOrWhiteSpace(_startUrl))
        {
            return;
        }

        try
        {
            var parentPlacement = OperatingSystem.IsWindows() ? WindowPlacement.Capture(parent.WindowHandle) : null;
            var placement = WindowPlacement.Resolve(parentPlacement, WindowPlacement.GetMonitors());
            var child = new PhotinoWindow(parent)
                .SetTitle("Glance")
                .SetUseOsDefaultSize(false)
                .SetSize(placement.Width, placement.Height)
                .SetUseOsDefaultLocation(false)
                .SetLocation(new Point(placement.X, placement.Y))
                .SetMinSize(MinWidth, MinHeight)
                .SetResizable(true);
            child.RegisterWebMessageReceivedHandler(HandleWebMessage);
            RegisterZoom(child);
            child.WindowCreated += (_, _) => RestoreWindowPlacement(child, parentPlacement);
            child.WindowClosing += (_, _) => RequestWindowClose(child);
            if (!string.IsNullOrWhiteSpace(_iconPath) && File.Exists(_iconPath))
            {
                child.SetIconFile(_iconPath);
            }
            lock (WindowGate)
            {
                Windows[child.Id] = new WindowState(child, isPrimary: false);
            }
            child.Load(_startUrl);
            child.WaitForClose();
            Log($"Opened secondary window {child.Id}.");
        }
        catch (Exception ex)
        {
            Log($"Unable to open secondary window: {ex}");
        }
    }

    private static bool RequestWindowClose(PhotinoWindow window)
    {
        lock (WindowGate)
        {
            if (!Windows.TryGetValue(window.Id, out var state))
            {
                return false;
            }
            if (state.AllowClose)
            {
                SaveWindowPlacement(window);
                Windows.Remove(window.Id);
                return false;
            }
        }

        BeginClose(window, restart: false);
        return true;
    }

    private static bool BeginClose(PhotinoWindow requester, bool restart)
    {
        CloseOperation? operation;
        lock (WindowGate)
        {
            if (_closeOperation is not null || _flushOperation is not null)
            {
                return false;
            }
            if (!Windows.TryGetValue(requester.Id, out var requesterState))
            {
                return false;
            }

            var targets = (restart || requesterState.IsPrimary)
                ? Windows.Values.ToArray()
                : new[] { requesterState };
            var requestId = $"close-{Guid.NewGuid():N}";
            operation = new CloseOperation(requestId, requester, targets, restart);
            _closeOperation = operation;
            operation.Timeout = new System.Threading.Timer(
                _ => CancelClose(operation, "Timed out while waiting for notes to finish saving."),
                null,
                TimeSpan.FromSeconds(45),
                Timeout.InfiniteTimeSpan);
        }

        foreach (var target in operation.Targets)
        {
            if (!TrySend(target.Window, new
                {
                    type = "prepareClose",
                    requestId = operation.RequestId,
                    reason = restart ? "restore" : "close"
                }))
            {
                CancelClose(operation, "A Glance window could not be contacted, so closing was cancelled.");
                return false;
            }
        }
        return true;
    }

    private static void HandleCloseResult(PhotinoWindow window, string requestId, bool ok, string? message)
    {
        CloseOperation? completed = null;
        lock (WindowGate)
        {
            var operation = _closeOperation;
            if (operation is null || !string.Equals(operation.RequestId, requestId, StringComparison.Ordinal) ||
                !operation.Pending.Remove(window.Id))
            {
                return;
            }
            if (!ok)
            {
                // Cancel outside the lock so messaging cannot re-enter this section.
                completed = operation;
            }
            else if (operation.Pending.Count == 0)
            {
                _closeOperation = null;
                operation.Timeout?.Dispose();
                _restartAfterClose = operation.Restart;
                foreach (var target in operation.Targets)
                {
                    target.AllowClose = true;
                }
                completed = operation;
            }
        }

        if (completed is null)
        {
            return;
        }
        if (!ok)
        {
            CancelClose(completed, message ?? "A note could not be saved. The window remains open.");
            return;
        }

        foreach (var target in completed.Targets.OrderBy(state => state.IsPrimary))
        {
            try
            {
                target.Window.Close();
            }
            catch (Exception ex)
            {
                Log($"Unable to close window {target.Window.Id}: {ex.Message}");
            }
        }
    }

    private static void CancelClose(CloseOperation operation, string message)
    {
        lock (WindowGate)
        {
            if (!ReferenceEquals(_closeOperation, operation))
            {
                return;
            }
            _closeOperation = null;
            operation.Timeout?.Dispose();
        }
        foreach (var target in operation.Targets)
        {
            TrySend(target.Window, new { type = "operationReleased", requestId = operation.RequestId });
        }
        var userMessage = operation.Restart
            ? $"{message} The restore remains staged and will apply the next time Glance restarts."
            : message;
        TrySend(operation.Requester, new { type = "closeCancelled", message = userMessage });
    }

    private static void BeginFlush(PhotinoWindow initiator, string clientRequestId, string reason)
    {
        FlushOperation? operation;
        lock (WindowGate)
        {
            if (_flushOperation is not null || _closeOperation is not null)
            {
                SendResult(initiator, "coordinateFlushResult", clientRequestId, false, "Another save or close operation is already in progress.");
                return;
            }
            var targets = Windows.Values.ToArray();
            operation = new FlushOperation($"flush-{Guid.NewGuid():N}", clientRequestId, initiator, targets);
            _flushOperation = operation;
            operation.Timeout = new System.Threading.Timer(
                _ => FinishFlush(operation, false, "Timed out while waiting for notes to finish saving."),
                null,
                TimeSpan.FromSeconds(45),
                Timeout.InfiniteTimeSpan);
        }

        foreach (var target in operation.Targets)
        {
            if (!TrySend(target.Window, new { type = "prepareFlush", requestId = operation.RequestId, reason }))
            {
                FinishFlush(operation, false, "A Glance window could not be contacted.");
                return;
            }
        }
    }

    private static void HandleFlushResult(PhotinoWindow window, string requestId, bool ok, string? message)
    {
        FlushOperation? operation;
        var complete = false;
        lock (WindowGate)
        {
            operation = _flushOperation;
            if (operation is null || !string.Equals(operation.RequestId, requestId, StringComparison.Ordinal) ||
                !operation.Pending.Remove(window.Id))
            {
                return;
            }
            complete = !ok || operation.Pending.Count == 0;
        }
        if (complete)
        {
            FinishFlush(operation, ok, ok ? null : message ?? "A note could not be saved.");
        }
    }

    private static void FinishFlush(FlushOperation operation, bool ok, string? message)
    {
        lock (WindowGate)
        {
            if (!ReferenceEquals(_flushOperation, operation))
            {
                return;
            }
            _flushOperation = null;
            operation.Timeout?.Dispose();
        }
        foreach (var target in operation.Targets)
        {
            TrySend(target.Window, new { type = "operationReleased", requestId = operation.RequestId });
        }
        SendResult(operation.Initiator, "coordinateFlushResult", operation.ClientRequestId, ok, message);
    }

    private static void PickFolder(PhotinoWindow window, string requestId, string? currentPath)
    {
        try
        {
            var defaultPath = !string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath)
                ? currentPath
                : null;
            var selected = window.ShowOpenFolder("Choose a second Glance backup location", defaultPath, multiSelect: false);
            TrySend(window, new
            {
                type = "pickFolderResult",
                requestId,
                ok = true,
                path = selected.FirstOrDefault()
            });
        }
        catch (Exception ex)
        {
            SendResult(window, "pickFolderResult", requestId, false, ex.Message);
        }
    }

    private static void RestartApplication(string appRoot)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                executable = Path.Combine(appRoot, "glance.exe");
            }
            if (!File.Exists(executable))
            {
                Log($"Restore was staged, but the executable could not be found at {executable}.");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = appRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Unable to restart Glance for restore: {ex}");
        }
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static void SendResult(PhotinoWindow window, string type, string requestId, bool ok, string? message) =>
        TrySend(window, new { type, requestId, ok, message });

    private static bool TrySend(PhotinoWindow window, object message)
    {
        try
        {
            window.SendWebMessage(JsonSerializer.Serialize(message));
            return true;
        }
        catch (Exception ex)
        {
            Log($"Unable to send a message to window {window.Id}: {ex.Message}");
            return false;
        }
    }

    private sealed class WindowState
    {
        public WindowState(PhotinoWindow window, bool isPrimary)
        {
            Window = window;
            IsPrimary = isPrimary;
        }

        public PhotinoWindow Window { get; }
        public bool IsPrimary { get; }
        public bool AllowClose { get; set; }
        public bool ZoomReady { get; set; }
        public string? ZoomMonitor { get; set; }
        public int ZoomPercent { get; set; } = 100;
        public double DisplayScale { get; set; }
    }

    private sealed class CloseOperation
    {
        public CloseOperation(string requestId, PhotinoWindow requester, IReadOnlyList<WindowState> targets, bool restart)
        {
            RequestId = requestId;
            Requester = requester;
            Targets = targets;
            Restart = restart;
            Pending = targets.Select(target => target.Window.Id).ToHashSet();
        }

        public string RequestId { get; }
        public PhotinoWindow Requester { get; }
        public IReadOnlyList<WindowState> Targets { get; }
        public bool Restart { get; }
        public HashSet<Guid> Pending { get; }
        public System.Threading.Timer? Timeout { get; set; }
    }

    private sealed class FlushOperation
    {
        public FlushOperation(string requestId, string clientRequestId, PhotinoWindow initiator, IReadOnlyList<WindowState> targets)
        {
            RequestId = requestId;
            ClientRequestId = clientRequestId;
            Initiator = initiator;
            Targets = targets;
            Pending = targets.Select(target => target.Window.Id).ToHashSet();
        }

        public string RequestId { get; }
        public string ClientRequestId { get; }
        public PhotinoWindow Initiator { get; }
        public IReadOnlyList<WindowState> Targets { get; }
        public HashSet<Guid> Pending { get; }
        public System.Threading.Timer? Timeout { get; set; }
    }
}
