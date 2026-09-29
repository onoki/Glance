using Glance.Desktop;
using System.Runtime.InteropServices;
namespace Glance.Server.Tests;
public class WindowPresentationTests
{
    [Theory]
    [InlineData(true, 1, "Glance", false)]
    [InlineData(true, 2, "G - Main", true)]
    [InlineData(false, 2, "G - Secondary", false)]
    [InlineData(true, 3, "G - Main", true)]
    public void CaptionsAndWarningFollowLiveWindowCount(bool main, int count, string title, bool warning)
    {
        Assert.Equal(title, WindowPresentation.Title(main, count));
        Assert.Equal(warning, WindowPresentation.NeedsCloseWarning(main, count));
    }
    [Fact]
    public void PortableShortcutCarriesMatchingIdentityAndCapitalizedName()
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() => {
            if (!OperatingSystem.IsWindows()) return;
            var folder = Path.Combine(Path.GetTempPath(), "GlanceShellTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            object? shell = null, directory = null, item = null;
            try
            {
                var executable = Path.Combine(folder, "glance.exe");
                File.WriteAllText(executable, "test fixture; not executable");
                WindowsShellIdentity.CreateShortcut(executable);
                WindowsShellIdentity.CreateShortcut(executable); // Refresh is safe after an update.
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                dynamic host = shell;
                directory = host.NameSpace(folder);
                dynamic location = directory;
                item = location.ParseName("Glance.lnk");
                dynamic shortcut = item;
                Assert.Equal(WindowsShellIdentity.AppId, (string)shortcut.ExtendedProperty("System.AppUserModel.ID"));
                Assert.True(File.Exists(Path.Combine(folder, "Glance.lnk")));
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (item != null) Marshal.FinalReleaseComObject(item);
                if (directory != null) Marshal.FinalReleaseComObject(directory);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
                Directory.Delete(folder, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }
}
