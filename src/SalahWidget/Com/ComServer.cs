using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace SalahWidget.Com;

internal static class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint REGCLS_MULTIPLEUSE = 0x1;

    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoRevokeClassObject(uint dwRegister);
}

[ComImport]
[ComVisible(false)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

/// <summary>COM class factory that hands the Widgets host a WinRT-projected provider.</summary>
[ComVisible(true)]
internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
            return CLASS_E_NOAGGREGATION;

        if (riid == typeof(T).GUID || riid == IID_IUnknown)
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return 0;
        }

        return E_NOINTERFACE;
    }

    public int LockServer(bool fLock) => 0;
}
