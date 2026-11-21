using System.Runtime.InteropServices;
using Glance.Desktop;

namespace Glance.Server.Tests;

public class NativeWindowPlacementTests
{
    [Fact]
    public void CustomTitleBarReclaimsCaptionSpaceAndKeepsSizingFrame()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int overlappedWindow = 0x00CF0000;
        const int caption = 0x00C00000;
        var handle = CreateWindowEx(0, "STATIC", "Glance caption regression", overlappedWindow,
            120, 140, 480, 320, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.True(GetClientRect(handle, out var before));
            var placement = WindowPlacement.Capture(handle);
            var style = GetWindowLong(handle, -16);
            Assert.True(WindowsTitleBar.TryEnable(handle));
            Assert.Equal(style & ~caption, GetWindowLong(handle, -16));
            Assert.True(GetClientRect(handle, out var after));
            Assert.True(after.Bottom > before.Bottom);
            Assert.True(after.Bottom < 320);
            Assert.Equal(placement, WindowPlacement.Capture(handle));
            Assert.NotEqual(IntPtr.Zero, GetSystemMenu(handle, false));
        }
        finally { DestroyWindow(handle); }
    }

    [Fact]
    public void CustomTitleBarMaximizesWithinEachMonitorsWorkArea()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var monitor in WindowPlacement.GetMonitors())
        {
            var handle = CreateWindowEx(0, "STATIC", "Glance maximize regression", 0x01CF0000,
                monitor.WorkArea.Left + 20, monitor.WorkArea.Top + 20, 480, 320,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, handle);
            try
            {
                var placement = WindowPlacement.Capture(handle);
                Assert.True(WindowsTitleBar.TryEnable(handle));
                Assert.True(GetWindowRect(handle, out var bounds));
                Assert.Equal(monitor.WorkArea.Left, bounds.Left);
                Assert.Equal(monitor.WorkArea.Top, bounds.Top);
                Assert.Equal(monitor.WorkArea.Right, bounds.Right);
                Assert.Equal(monitor.WorkArea.Bottom, bounds.Bottom);
                Assert.Equal(placement, WindowPlacement.Capture(handle));
            }
            finally { DestroyWindow(handle); }
        }
    }

    [Theory]
    [InlineData(640, 240)]
    [InlineData(427, 160)]
    [InlineData(-640, -240)]
    public void MaximizedBoundsAreCorrectedAfterWindowsAdjustsForDifferentMonitorSizes(int extraWidth, int extraHeight)
    {
        if (!OperatingSystem.IsWindows()) return;
        // 1920x1200 -> 2560x1440 adds 640x240 physical pixels, or about
        // 427x160 logical pixels at 150%. Exercise the final native resize,
        // rather than only querying WM_GETMINMAXINFO before Windows adjusts it.
        foreach (var monitor in WindowPlacement.GetMonitors())
        {
            var handle = CreateWindowEx(0, "STATIC", "Glance monitor size regression", 0x01CF0000,
                monitor.WorkArea.Left + 20, monitor.WorkArea.Top + 20, 480, 320,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, handle);
            try
            {
                Assert.True(WindowsTitleBar.TryEnable(handle));
                var placement = WindowPlacement.Capture(handle);
                Assert.True(SetWindowPos(handle, IntPtr.Zero, monitor.WorkArea.Left, monitor.WorkArea.Top,
                    monitor.WorkArea.Width + extraWidth, monitor.WorkArea.Height + extraHeight, 0x0014));
                Assert.True(GetWindowRect(handle, out var actual));
                Assert.Equal(monitor.WorkArea.Left, actual.Left);
                Assert.Equal(monitor.WorkArea.Top, actual.Top);
                Assert.Equal(monitor.WorkArea.Right, actual.Right);
                Assert.Equal(monitor.WorkArea.Bottom, actual.Bottom);
                Assert.Equal(placement, WindowPlacement.Capture(handle));
            }
            finally { DestroyWindow(handle); }
        }
    }

    [Fact]
    public void MaximizedWindowMovesToTheDestinationMonitorsWorkArea()
    {
        if (!OperatingSystem.IsWindows()) return;
        var monitors = WindowPlacement.GetMonitors();
        var first = monitors[0].WorkArea;
        var handle = CreateWindowEx(0, "STATIC", "Glance maximized monitor move regression", 0x01CF0000,
            first.Left + 20, first.Top + 20, 480, 320,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.True(WindowsTitleBar.TryEnable(handle));
            var placement = WindowPlacement.Capture(handle);
            foreach (var monitor in monitors.Concat(monitors.Reverse()))
            {
                var area = monitor.WorkArea;
                // Moving alone uses the destination monitor, even though the old
                // window dimensions can belong to a different-sized display.
                Assert.True(SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0015));
                Assert.True(GetWindowRect(handle, out var bounds));
                Assert.Equal(area.Left, bounds.Left);
                Assert.Equal(area.Top, bounds.Top);
                Assert.Equal(area.Right, bounds.Right);
                Assert.Equal(area.Bottom, bounds.Bottom);
                // A size-only request ignores its x/y arguments.
                Assert.True(SetWindowPos(handle, IntPtr.Zero, -32000, -32000, area.Width + 640, area.Height + 240, 0x0016));
                Assert.True(GetWindowRect(handle, out bounds));
                Assert.Equal(area.Left, bounds.Left);
                Assert.Equal(area.Top, bounds.Top);
                Assert.Equal(area.Right, bounds.Right);
                Assert.Equal(area.Bottom, bounds.Bottom);
            }
            // Return to the original monitor before comparing restore coordinates.
            Assert.True(SetWindowPos(handle, IntPtr.Zero, first.Left, first.Top, 0, 0, 0x0015));
            Assert.Equal(placement, WindowPlacement.Capture(handle));
        }
        finally { DestroyWindow(handle); }
    }

    [Fact]
    public void MinimizedWindowKeepsItsOffScreenPosition()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = CreateWindowEx(0, "STATIC", "Glance minimized regression", 0x21CF0000,
            120, 140, 480, 320, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.True(WindowsTitleBar.TryEnable(handle));
            Assert.True(SetWindowPos(handle, IntPtr.Zero, -32000, -32000, 160, 28, 0x0014));
            Assert.True(GetWindowRect(handle, out var bounds));
            Assert.Equal(-32000, bounds.Left);
            Assert.Equal(-32000, bounds.Top);
        }
        finally { DestroyWindow(handle); }
    }

    [Theory]
    [InlineData(140, 180)]
    [InlineData(-600, -400)]
    public void NativeRestoreRoundTripsSignedScreenCoordinates(int x, int y)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Invisible test-owned window; no desktop interaction or user state.
        var handle = CreateWindowEx(0, "STATIC", "Glance placement regression", 0x00CF0000,
            0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            WindowPlacement.Apply(handle, new(x, y, 480, 320, null));
            var saved = WindowPlacement.Capture(handle);
            Assert.NotNull(saved);
            Assert.Equal(x, saved.X);
            Assert.Equal(y, saved.Y);
            Assert.Equal(480, saved.Width);
            Assert.Equal(320, saved.Height);
            Assert.NotEmpty(saved.Monitor!);
        }
        finally { DestroyWindow(handle); }
    }

    [Fact]
    public void MaximizedClientAreaReachesTheWorkAreaEdges()
    {
        if (!OperatingSystem.IsWindows()) return;
        var monitor = WindowPlacement.GetMonitors().First(item => item.Primary);
        var handle = CreateWindowEx(0, "STATIC", "Glance maximized client regression", 0x01CF0000,
            monitor.WorkArea.Left, monitor.WorkArea.Top, 480, 320,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.True(WindowsTitleBar.TryEnable(handle));
            Assert.True(WindowsTitleBar.IsZoomed(handle));
            Assert.True(SetWindowPos(handle, IntPtr.Zero, monitor.WorkArea.Left, monitor.WorkArea.Top,
                monitor.WorkArea.Width, monitor.WorkArea.Height, 0x0014));
            Assert.True(GetClientRect(handle, out var client));
            var origin = new Point();
            Assert.True(ClientToScreen(handle, ref origin));
            Assert.Equal(monitor.WorkArea.Left, origin.X);
            Assert.Equal(monitor.WorkArea.Top, origin.Y);
            Assert.Equal(monitor.WorkArea.Width, client.Right);
            Assert.Equal(monitor.WorkArea.Height, client.Bottom);
        }
        finally { DestroyWindow(handle); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
}
