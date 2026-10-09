using System.Runtime.InteropServices;
using System.Windows.Automation;
using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class EdgeDockUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Ui")]
    public void Edge_pet_collapses_reveals_on_hover_and_keeps_bubble_readable(bool right)
    {
        var exe = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.True(System.IO.File.Exists(exe), "Set DESKTOP_PUPPY_EXE to the Release executable.");
        using var harness = DesktopPuppyUiHarness.Start(exe!);
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(5));
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        using var displays = new Win32MonitorDisplayProvider();
        var area = displays.GetMonitors().First(m => m.IsPrimary).WorkArea;
        var api = new Win32WindowApi();
        Assert.True(api.TryGetWindowRect(handle, out var bounds));
        var scale = api.GetDpiForWindow(handle) / 96d;
        SetCursorPos(area.Left + area.Width / 2, area.Top + 20);
        var x = right ? area.Right - bounds.Width + (int)(95 * scale) : area.Left - (int)(95 * scale);
        Assert.True(api.SetWindowPosition(handle, new PhysicalRect(x, area.Top + 150, x + bounds.Width, area.Top + 150 + bounds.Height)));
        var side = right ? "right" : "left";
        try
        {
            harness.WaitForAutomationName(window, "PetAnimation", n => n == $"edge-peek-{side}", TimeSpan.FromSeconds(6));
        }
        catch (TimeoutException ex)
        {
            api.TryGetWindowRect(handle, out var actual);
            throw new InvalidOperationException($"Dock {side}: work={area}, rect={actual}, scale={scale}, status={window.Current.ItemStatus}, image={harness.FindByAutomationId(window, "PetAnimation").Current.BoundingRectangle}", ex);
        }
        Assert.True(api.TryGetWindowRect(handle, out bounds));
        WaitForCollapsed(window);
        Assert.True(bounds.Left >= area.Left && bounds.Right <= area.Right, "Dock window must stay inside its display.");
        // Hover the exposed muzzle, not the transparent center of the transformed image.
        SetCursorPos(right ? area.Right - (int)(18 * scale) : area.Left + (int)(18 * scale), bounds.Top + (int)(129 * scale));
        harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(3));
        var button = harness.FindByAutomationId(window, "StatisticsButton");
        Assert.False(button.Current.IsOffscreen);
        harness.SingleClickCenter();
        var bubble = harness.WaitForAutomationId(window, "SpeechBubbleText", TimeSpan.FromSeconds(3));
        Assert.False(string.IsNullOrWhiteSpace(bubble.Current.Name));
        SetCursorPos(area.Left + area.Width / 2, area.Top + 20);
        Thread.Sleep(1200);
        Assert.False(bubble.Current.IsOffscreen);
        Assert.True(api.TryGetWindowRect(handle, out bounds));
        var bubbleBounds = bubble.Current.BoundingRectangle;
        Assert.True(bubbleBounds.Left >= area.Left && bubbleBounds.Right <= area.Right);
        ((InvokePattern)harness.FindByAutomationId(window, "CloseBubbleButton").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        harness.WaitForAutomationName(window, "PetAnimation", n => n == $"edge-peek-{side}", TimeSpan.FromSeconds(4));
        WaitForCollapsed(window);
        var imageBefore = harness.FindByAutomationId(window, "PetAnimation").Current.BoundingRectangle;
        var startX = right ? area.Right - (int)(18 * scale) : area.Left + (int)(18 * scale);
        var startY = bounds.Top + (int)(129 * scale);
        var deltaX = (int)(300 * scale) * (right ? -1 : 1);
        harness.Drag((startX, startY), (startX + deltaX, startY + (int)(60 * scale)));
        harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
        Assert.Equal("dock-None-Free", window.Current.ItemStatus);
        var imageAfter = harness.FindByAutomationId(window, "PetAnimation").Current.BoundingRectangle;
        var expectedCenter = imageBefore.Left + imageBefore.Width / 2 + deltaX;
        var actualCenter = imageAfter.Left + imageAfter.Width / 2;
        Assert.True(Math.Abs(actualCenter - expectedCenter) <= 40 * scale,
            $"Expected center {expectedCenter}, actual {actualCenter}, before={imageBefore}, after={imageAfter}; {window.Current.HelpText}");
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private static void WaitForCollapsed(AutomationElement window)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (window.Current.ItemStatus.EndsWith("-Collapsed", StringComparison.Ordinal)) return;
            Thread.Sleep(20);
        }
        Assert.Fail($"Dock did not finish collapsing: {window.Current.ItemStatus}");
    }
}
