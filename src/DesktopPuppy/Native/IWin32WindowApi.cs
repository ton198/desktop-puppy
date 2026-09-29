namespace DesktopPuppy.Native;

public interface IWin32WindowApi
{
    bool TryGetWindowRect(IntPtr hwnd, out PhysicalRect bounds);
    IntPtr GetMonitorFromWindow(IntPtr hwnd);
    uint GetDpiForWindow(IntPtr hwnd);
    bool SetWindowPosition(IntPtr hwnd, PhysicalRect bounds);
}
