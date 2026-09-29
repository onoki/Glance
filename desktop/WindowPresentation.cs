using System.Runtime.InteropServices;
namespace Glance.Desktop;
internal static class WindowPresentation
{
    public static string Title(bool primary, int count) => count <= 1 ? "Glance" : primary ? "G - Main" : "G - Secondary";
    public static bool NeedsCloseWarning(bool primary, int count) => primary && count > 1;
    public static bool ConfirmCloseAll(IntPtr owner) => MessageBox(owner,
        "Closing the main window will close all Glance windows. Notes will be saved before closing.\n\nClose all Glance windows?",
        "Glance", 0x134) == 6; // Yes/No, warning, default No.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
