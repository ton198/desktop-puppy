namespace DesktopPuppy.Native;

public interface INativeWindowPositioner
{
    PhysicalRect GetBounds(IntPtr hwnd);
    MonitorDisplayInfo GetMonitorForWindow(IntPtr hwnd);
    uint GetDpi(IntPtr hwnd);
    void SetBounds(IntPtr hwnd, PhysicalRect bounds);
}
