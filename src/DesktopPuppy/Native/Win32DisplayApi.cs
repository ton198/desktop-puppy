using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopPuppy.Native;

public sealed class Win32DisplayApi : IWin32DisplayApi
{
    private const int CchDeviceName = 32;

    public bool EnumerateMonitors(Func<IntPtr, bool> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Exception? callbackFailure = null;
        MonitorEnumProc nativeCallback = (monitor, _, _, _) =>
        {
            try
            {
                return callback(monitor);
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                return false;
            }
        };

        var succeeded = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, nativeCallback, IntPtr.Zero);
        GC.KeepAlive(nativeCallback);
        if (callbackFailure is not null)
        {
            throw new InvalidOperationException("A monitor enumeration callback failed.", callbackFailure);
        }

        return succeeded;
    }

    public bool TryGetMonitorInfo(IntPtr monitorHandle, out NativeMonitorInfo info)
    {
        var nativeInfo = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>()
        };

        if (!GetMonitorInfo(monitorHandle, ref nativeInfo))
        {
            info = default;
            return false;
        }

        info = new NativeMonitorInfo(
            nativeInfo.Device,
            new PhysicalRect(nativeInfo.Monitor.Left, nativeInfo.Monitor.Top, nativeInfo.Monitor.Right, nativeInfo.Monitor.Bottom),
            new PhysicalRect(nativeInfo.Work.Left, nativeInfo.Work.Top, nativeInfo.Work.Right, nativeInfo.Work.Bottom),
            (nativeInfo.Flags & 1) != 0);
        return true;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string Device;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
