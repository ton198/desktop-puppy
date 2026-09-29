using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopPuppy.Native;

public sealed class Win32WindowApi : IWin32WindowApi
{
    private const uint MonitorDefaultToNull = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public bool TryGetWindowRect(IntPtr hwnd, out PhysicalRect bounds)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            bounds = default;
            return false;
        }

        bounds = new PhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    public IntPtr GetMonitorFromWindow(IntPtr hwnd) => MonitorFromWindow(hwnd, MonitorDefaultToNull);

    public uint GetDpiForWindow(IntPtr hwnd) => GetDpiForWindowNative(hwnd);

    public bool SetWindowPosition(IntPtr hwnd, PhysicalRect bounds) => SetWindowPos(
        hwnd,
        IntPtr.Zero,
        bounds.Left,
        bounds.Top,
        bounds.Width,
        bounds.Height,
        SwpNoActivate | SwpNoZOrder);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static extern uint GetDpiForWindowNative(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
