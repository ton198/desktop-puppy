using System.ComponentModel;

namespace DesktopPuppy.Native;

public sealed class Win32WindowPositioner : INativeWindowPositioner
{
    private readonly IWin32WindowApi _windowApi;
    private readonly Win32MonitorDisplayProvider _displayProvider;

    public Win32WindowPositioner(IWin32WindowApi windowApi, Win32MonitorDisplayProvider displayProvider)
    {
        ArgumentNullException.ThrowIfNull(windowApi);
        ArgumentNullException.ThrowIfNull(displayProvider);
        _windowApi = windowApi;
        _displayProvider = displayProvider;
    }

    public PhysicalRect GetBounds(IntPtr hwnd)
    {
        if (!_windowApi.TryGetWindowRect(hwnd, out var bounds))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "GetWindowRect failed.");
        }

        if (!bounds.IsValid)
        {
            throw new InvalidOperationException("GetWindowRect returned invalid physical bounds.");
        }

        return bounds;
    }

    public MonitorDisplayInfo GetMonitorForWindow(IntPtr hwnd)
    {
        var monitorHandle = _windowApi.GetMonitorFromWindow(hwnd);
        if (monitorHandle == IntPtr.Zero)
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "MonitorFromWindow could not identify a monitor for the HWND.");
        }

        return _displayProvider.GetMonitorForHandle(monitorHandle);
    }

    public uint GetDpi(IntPtr hwnd)
    {
        var dpi = _windowApi.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "GetDpiForWindow returned zero.");
        }

        return dpi;
    }

    public void SetBounds(IntPtr hwnd, PhysicalRect bounds)
    {
        if (!bounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Window bounds must have positive physical extents.");
        }

        if (!_windowApi.SetWindowPosition(hwnd, bounds))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        }
    }
}
