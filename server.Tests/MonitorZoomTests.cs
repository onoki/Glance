using Glance.Desktop;
namespace Glance.Server.Tests;

public class MonitorZoomTests
{
    [Fact]
    public void PreferencesRemainDistinctAcrossMonitorsAndRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GlanceZoomTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "zoom.json");
        try
        {
            var preferences = new ZoomPreferences(path);
            Assert.Equal(100, preferences.Get("unknown"));
            preferences.Save("left", 120);
            preferences.Save("right", 180);
            preferences = new ZoomPreferences(path);
            Assert.Equal(120, preferences.Get("left"));
            Assert.Equal(180, preferences.Get("right"));
            preferences.Save("left", 100);
            preferences = new ZoomPreferences(path);
            Assert.Equal(100, preferences.Get("left"));
            Assert.Equal(180, preferences.Get("right"));
            preferences.Save("invalid-low", -1);
            preferences.Save("invalid-high", 9999);
            Assert.Equal(50, preferences.Get("invalid-low"));
            Assert.Equal(400, preferences.Get("invalid-high"));
            File.WriteAllText(path, "broken");
            Assert.Equal(100, new ZoomPreferences(path).Get("left"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
