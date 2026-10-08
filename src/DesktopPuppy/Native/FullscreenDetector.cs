using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPuppy.Native;

public sealed record FullscreenWindowInfo(PhysicalRect Bounds, bool Visible, bool Minimized, bool Cloaked, bool Framed, bool IsDesktop);
public static class FullscreenPolicy
{
    public static bool IsBorderlessFullscreen(FullscreenWindowInfo window, IReadOnlyList<PhysicalRect> monitors) => window.Visible && !window.Minimized && !window.Cloaked && !window.Framed && !window.IsDesktop && monitors.Any(m =>
        Math.Abs((long)m.Left - window.Bounds.Left) <= 1 && Math.Abs((long)m.Top - window.Bounds.Top) <= 1 && Math.Abs((long)m.Right - window.Bounds.Right) <= 1 && Math.Abs((long)m.Bottom - window.Bounds.Bottom) <= 1);
    public static bool ShouldSuppress(int shellState, IReadOnlyList<FullscreenWindowInfo> windows, IReadOnlyList<PhysicalRect> monitors) => shellState == 3 || windows.Any(w => IsBorderlessFullscreen(w, monitors));
}

public interface IFullscreenDetector { bool IsFullscreen(); }
public sealed class Win32FullscreenDetector(IMonitorDisplayProvider monitors) : IFullscreenDetector
{
    public bool IsFullscreen()
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state == 3) return true;
        var displays = monitors.GetMonitors().Select(m => m.Bounds).ToArray(); bool fullscreen = false;
        EnumProc callback = (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid); if (pid == (uint)Environment.ProcessId) return true;
            if (!GetWindowRect(hwnd, out var rect)) return true;
            var style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -16).ToInt64() : GetWindowLong(hwnd, -16);
            var framed = (style & (0x00C00000L | 0x00040000L | 0x01000000L)) != 0;
            DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int));
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            var desktop = hwnd == GetShellWindow() || hwnd == GetDesktopWindow() || name.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
            if (FullscreenPolicy.IsBorderlessFullscreen(new(new(rect.Left, rect.Top, rect.Right, rect.Bottom), true, false, cloaked != 0, framed, desktop), displays)) fullscreen = true;
            return !fullscreen;
        };
        EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback); return fullscreen;
    }
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
