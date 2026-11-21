using System.Runtime.InteropServices;

namespace Glance.Desktop;

internal static class WindowsTitleBar
{
    private const int WindowStyle = -16;
    private const int Caption = 0x00C00000;
    private const uint FrameChanged = 0x0020;
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint KeepBoundsAndOrder = NoSize | NoMove | 0x0004 | 0x0010;
    private const uint Close = 0x0010;
    private const uint SystemCommand = 0x0112;
    private const uint NonClientLeftDown = 0x00A1;
    private const int HitCaption = 2;
    private const int LeftButton = 0x01;
    private const int Minimize = 0xF020;
    private const int Maximize = 0xF030;
    private const int Restore = 0xF120;
    private const uint MenuRightButton = 0x0002;
    private const uint MenuReturnCommand = 0x0100;
    private const uint WindowPositionChanging = 0x0046;
    private const uint NonClientDestroy = 0x0082;
    private const uint NonClientCalculateSize = 0x0083;
    private const nuint FrameSubclassId = 1;
    private static readonly SubclassProc FrameSubclass = HandleWindowMessage;

    // Keep the existing sizing frame and window styles used by Windows for
    // maximize, minimize and snapping. Only the standard caption is replaced.
    internal static bool TryEnable(nint window)
    {
        var style = GetWindowLong(window, WindowStyle);
        if ((style & Caption) != Caption) return false;
        if (!SetWindowSubclass(window, FrameSubclass, FrameSubclassId, 0)) return false;
        if (SetWindowLong(window, WindowStyle, style & ~Caption) == 0)
        {
            RemoveWindowSubclass(window, FrameSubclass, FrameSubclassId);
            return false;
        }
        if (SetWindowPos(window, 0, 0, 0, 0, 0, KeepBoundsAndOrder | FrameChanged)) return true;
        SetWindowLong(window, WindowStyle, style);
        SetWindowPos(window, 0, 0, 0, 0, 0, KeepBoundsAndOrder | FrameChanged);
        RemoveWindowSubclass(window, FrameSubclass, FrameSubclassId);
        return false;
    }

    private static nint HandleWindowMessage(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == NonClientDestroy)
            RemoveWindowSubclass(window, FrameSubclass, subclassId);

        // The maximized outer bounds already equal the work area. Suppress the
        // normal resize-frame inset so the WebView reaches those edges as well.
        if (message == NonClientCalculateSize && wParam != 0 && IsZoomed(window) && !IsIconic(window)) return 0;

        // Let Windows and Photino apply sizing rules before correcting the final
        // position. WM_GETMINMAXINFO uses primary-monitor coordinates, which
        // Windows later adjusts when maximizing onto a different-sized monitor.
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message != WindowPositionChanging || !IsZoomed(window) || IsIconic(window)) return result;
        var position = Marshal.PtrToStructure<WindowPosition>(lParam);
        if ((position.Flags & (NoSize | NoMove)) == (NoSize | NoMove)
            && (position.Flags & FrameChanged) == 0) return result;
        if (!GetWindowRect(window, out var current)) return result;
        var x = (position.Flags & NoMove) == 0 ? position.X : current.Left;
        var y = (position.Flags & NoMove) == 0 ? position.Y : current.Top;
        var width = (position.Flags & NoSize) == 0 ? position.Width : current.Right - current.Left;
        var height = (position.Flags & NoSize) == 0 ? position.Height : current.Bottom - current.Top;
        var destination = new Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref destination, 2), ref monitor)) return result;

        position.X = monitor.Work.Left;
        position.Y = monitor.Work.Top;
        position.Width = monitor.Work.Right - monitor.Work.Left;
        position.Height = monitor.Work.Bottom - monitor.Work.Top;
        position.Flags &= ~(NoSize | NoMove);
        Marshal.StructureToPtr(position, lParam, false);
        return 0;
    }

    internal static void RunCommand(nint window, string? command)
    {
        switch (command)
        {
            case "move" when GetForegroundWindow() == window && (GetAsyncKeyState(LeftButton) & 0x8000) != 0:
                if (GetCursorPos(out var cursor))
                {
                    ReleaseCapture();
                    PostMessage(window, NonClientLeftDown, HitCaption, MousePosition(cursor));
                }
                break;
            case "minimize":
                PostMessage(window, SystemCommand, Minimize, 0);
                break;
            case "toggleMaximize":
                PostMessage(window, SystemCommand, IsZoomed(window) ? Restore : Maximize, 0);
                break;
            case "close":
                PostMessage(window, Close, 0, 0);
                break;
            case "menu":
                var menu = GetSystemMenu(window, false);
                if (menu != 0 && GetCursorPos(out var point))
                {
                    var selection = TrackPopupMenu(menu, MenuRightButton | MenuReturnCommand, point.X, point.Y, 0, window, 0);
                    if (selection != 0) PostMessage(window, SystemCommand, selection, MousePosition(point));
                }
                break;
        }
    }

    private static nint MousePosition(Point point) => (ushort)point.X | ((nint)(ushort)point.Y << 16);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect bounds, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint GetSystemMenu(nint window, bool revert);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint exclude);
}
