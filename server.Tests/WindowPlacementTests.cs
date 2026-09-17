using System.Drawing;
using Glance.Desktop;

namespace Glance.Server.Tests;

public class WindowPlacementTests
{
    private static readonly WindowMonitor Primary = new("primary", new(0, 0, 1920, 1080), new(0, 40, 1920, 1040), true);
    private static readonly WindowMonitor Secondary = new("secondary", new(-1280, 0, 1280, 1024), new(-1280, 0, 1280, 984), false);

    [Fact]
    public void NormalPositionAndSizeAreRestoredOnExistingMonitor()
    {
        var saved = new SavedWindowPlacement(-1200, 30, 900, 700, "secondary");
        Assert.Equal(saved, WindowPlacement.Resolve(saved, [Primary, Secondary]));
    }

    [Fact]
    public void MissingMonitorFallsBackToPrimaryAndFitsWorkArea()
    {
        var actual = WindowPlacement.Resolve(new(-2200, -300, 2500, 1500, "removed"), [Primary, Secondary]);
        Assert.Equal(new SavedWindowPlacement(0, 40, 1920, 1040, "primary"), actual);
    }

    [Fact]
    public void SmallerMonitorClampsBothSizeAndPosition()
    {
        var smaller = new WindowMonitor("secondary", new(-800, 0, 800, 600), new(-800, 0, 800, 560), false);
        Assert.Equal(new SavedWindowPlacement(-800, 0, 800, 560, "secondary"),
            WindowPlacement.Resolve(new(-1200, 100, 900, 700, "secondary"), [Primary, smaller]));
    }

    [Fact]
    public void SameMonitorMovedInDesktopLayoutKeepsWindowVisible()
    {
        var actual = WindowPlacement.Resolve(new(2000, 1500, 400, 300, "primary"), [Primary]);
        Assert.Equal(new SavedWindowPlacement(1520, 780, 400, 300, "primary"), actual);
    }

    [Fact]
    public void LegacySizeIsCenteredAndInvalidDimensionsAreBounded()
    {
        Assert.Equal(new SavedWindowPlacement(360, 160, 1200, 800, "primary"), WindowPlacement.Resolve(new(0, 0, 1200, 800, null), [Primary]));
        var actual = WindowPlacement.Resolve(new(0, 0, -1, 0, "primary"), [Primary]);
        Assert.Equal(new Size(100, 100), new Size(actual.Width, actual.Height));
    }
}
