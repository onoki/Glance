using System.Runtime.InteropServices;
using Glance.Desktop;
namespace Glance.Server.Tests;
public sealed class WindowIdentityTests
{
    [Fact]
    public void MainAndSecondaryUseShortcutIdentityAndReleasePropertiesOnClose()
    {
        if (!OperatingSystem.IsWindows()) return;
        var windows = Enumerable.Range(0,2).Select(_=>CreateWindowEx(0,"STATIC","Glance identity test",0,0,0,100,100,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)).ToArray();
        try
        {
            foreach (var window in windows)
            {
                Assert.NotEqual(IntPtr.Zero,window);
                WindowsShellIdentity.ApplyToWindow(window);
                WindowsShellIdentity.ApplyToWindow(window);
                Assert.Equal(WindowsShellIdentity.AppId,ReadIdentity(window));
                WindowsShellIdentity.ClearWindow(window);
                Assert.Null(ReadIdentity(window));
            }
        }
        finally { foreach(var window in windows) if(window!=IntPtr.Zero) DestroyWindow(window); }
    }
    private static string? ReadIdentity(IntPtr window)
    {
        var iid=typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out var store));
        var key=new PropertyKey {Format=new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};
        PropVariant value=default;
        try
        {
            Marshal.ThrowExceptionForHR(store.GetValue(ref key,out value));
            return value.Type==31?Marshal.PtrToStringUni(value.Pointer):null;
        }
        finally {PropVariantClear(ref value);if(OperatingSystem.IsWindows())Marshal.FinalReleaseComObject(store);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey {public Guid Format;public uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)] private struct PropVariant {[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);
        [PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);[PreserveSig]int Commit();
    }
    [DllImport("shell32.dll")]private static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,out IPropertyStore store);
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr window);
}
