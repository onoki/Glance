using System.Drawing;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Glance.Desktop;

internal record SavedWindowPlacement(int X, int Y, int Width, int Height, string? Monitor);
internal record WindowMonitor(string Id, Rectangle Bounds, Rectangle WorkArea, bool Primary);

internal static class WindowPlacement
{
    public static void Apply(IntPtr handle, SavedWindowPlacement placement)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Apply outer bounds together so Windows cannot choose a cascade position
        // or adjust size based on the monitor it initially selected.
        if (!SetWindowPos(handle, IntPtr.Zero, placement.X, placement.Y, placement.Width, placement.Height, 0x0014))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static SavedWindowPlacement Resolve(SavedWindowPlacement? saved, IReadOnlyList<WindowMonitor> monitors)
    {
        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors.First();
        var monitor = saved?.Monitor is { } id
            ? monitors.FirstOrDefault(m => m.Id == id) ?? primary
            : primary;
        var area = monitor.WorkArea;
        var width = Math.Clamp(saved?.Width ?? 1200, Math.Min(100, area.Width), area.Width);
        var height = Math.Clamp(saved?.Height ?? 800, Math.Min(100, area.Height), area.Height);
        var restorePosition = saved != null && saved.Monitor == monitor.Id;
        var x = restorePosition ? saved!.X : area.X + (area.Width - width) / 2;
        var y = restorePosition ? saved!.Y : area.Y + (area.Height - height) / 2;
        return new(Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(y, area.Top, area.Bottom - height), width, height, monitor.Id);
    }

    public static IReadOnlyList<WindowMonitor> GetMonitors()
    {
        var monitors = new List<WindowMonitor>();
        if (OperatingSystem.IsWindows())
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref NativeRect rect, IntPtr data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
                if (GetMonitorInfo(handle, ref info))
                    monitors.Add(new(info.Device, info.Monitor.ToRectangle(), info.Work.ToRectangle(), (info.Flags & 1) != 0));
                return true;
            }, IntPtr.Zero);
        }
        if (monitors.Count == 0) monitors.Add(new("default", new(0, 0, 1200, 800), new(0, 0, 1200, 800), true));
        return monitors;
    }

    public static SavedWindowPlacement? Capture(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero) return null;
        var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        if (!GetWindowPlacement(handle, ref placement)) return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return null;
        // GetWindowPlacement uses workspace coordinates for ordinary top-level windows.
        var normal = placement.Normal.ToRectangle();
        return new(normal.X + info.Work.Left - info.Monitor.Left,
            normal.Y + info.Work.Top - info.Monitor.Top, normal.Width, normal.Height, info.Device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public NativeRect Normal;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref NativePlacement placement);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
