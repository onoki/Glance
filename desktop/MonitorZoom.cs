using System.Runtime.InteropServices;
using System.Text.Json;

namespace Glance.Desktop;

// Deliberately PC-local: portable databases and backups must not carry display preferences.
internal static class MonitorZoom
{
    private static readonly ZoomPreferences Preferences = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance", "monitor-zoom.json"));
    public static int Get(string monitor) => Preferences.Get(monitor);
    public static void Save(string monitor, int zoom) => Preferences.Save(monitor, zoom);
    public static (string Id, double Scale) Identify(IntPtr window)
    {
        if (!OperatingSystem.IsWindows()) return ("default", 1);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return ("default", 1);
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        // Interface identity survives display-number changes when cables/monitors change.
        var id = EnumDisplayDevices(info.Device, 0, ref device, 1) && !string.IsNullOrWhiteSpace(device.DeviceId)
            ? device.DeviceId : info.Device;
        return (id, Math.Max(96u, GetDpiForWindow(window)) / 96.0);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
}

internal sealed class ZoomPreferences
{
    private readonly string _path;
    private readonly Dictionary<string, int> _preferences;
    public ZoomPreferences(string path)
    {
        _path = path;
        try { _preferences = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new(); }
        catch { _preferences = new(); }
    }
    public int Get(string monitor) => Math.Clamp(_preferences.GetValueOrDefault(monitor, 100), 50, 400);
    public void Save(string monitor, int zoom)
    {
        _preferences[monitor] = Math.Clamp(zoom, 50, 400);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_preferences));
        File.Move(temporary, _path, true);
    }
}
