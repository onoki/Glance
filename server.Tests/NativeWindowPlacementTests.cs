using System.Runtime.InteropServices;
using Glance.Desktop;

namespace Glance.Server.Tests;

public class NativeWindowPlacementTests
{
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
}
