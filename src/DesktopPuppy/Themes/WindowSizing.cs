using System.Windows;
using DesktopPuppy.Native;

namespace DesktopPuppy.Themes;

internal static class WindowSizing
{
    internal static void FitToWorkArea(Window window)
    {
        // CenterScreen uses the cursor's monitor for an unowned window. Screen bounds
        // are physical pixels, so convert using that monitor rather than primary DPI.
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        var dpi = MonitorDpi.At(new PhysicalRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), 96);
        var width = area.Width * 96d / dpi;
        var height = area.Height * 96d / dpi;
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = Math.Min(window.Width, width);
        window.Height = Math.Min(window.Height, height);
    }
}
