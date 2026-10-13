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
        harness.RevealPetAndWaitForIdle(window, TimeSpan.FromSeconds(5));
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        using var displays = new Win32MonitorDisplayProvider();
        var area = displays.GetMonitors().First(m => m.IsPrimary).WorkArea;
        var api = new Win32WindowApi();
        if (window.Current.ItemStatus.StartsWith("dock-Left-") || window.Current.ItemStatus.StartsWith("dock-Right-"))
        {
            var grab = harness.GetPetInteractionPoint();
            harness.Drag(grab, (area.Left + area.Width / 2, area.Top + area.Height / 2));
            harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
            Assert.Equal("dock-None-Free", window.Current.ItemStatus);
        }
        Assert.True(api.TryGetWindowRect(handle, out var bounds));
        var scale = api.GetDpiForWindow(handle) / 96d;
        SetCursorPos(area.Left + area.Width / 2, area.Top + 20);
        var x = right ? area.Right - bounds.Width : area.Left;
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
        SetCursorPos(right ? area.Right - (int)(18 * scale) : area.Left + (int)(18 * scale), bounds.Top + (int)(bounds.Height * .48));
        harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(3));
        harness.SingleClickCenter();
        var bubble = harness.WaitForAutomationId(harness.WaitForSpeechWindow(), "SpeechBubbleText", TimeSpan.FromSeconds(3));
        Assert.False(string.IsNullOrWhiteSpace(bubble.Current.Name));
        SetCursorPos(area.Left + area.Width / 2, area.Top + 20);
        Thread.Sleep(1200);
        Assert.False(bubble.Current.IsOffscreen);
        Assert.True(api.TryGetWindowRect(handle, out bounds));
        var bubbleBounds = bubble.Current.BoundingRectangle;
        Assert.True(bubbleBounds.Left >= area.Left && bubbleBounds.Right <= area.Right);
        ((InvokePattern)harness.FindByAutomationId(harness.WaitForSpeechWindow(), "CloseBubbleButton").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        harness.WaitForAutomationName(window, "PetAnimation", n => n == $"edge-peek-{side}", TimeSpan.FromSeconds(4));
        WaitForCollapsed(window);
        var startX = right ? area.Right - (int)(18 * scale) : area.Left + (int)(18 * scale);
        var startY = bounds.Top + (int)(bounds.Height * .48);
        var deltaX = (int)(300 * scale) * (right ? -1 : 1);
        harness.Drag((startX, startY), (startX + deltaX, startY + (int)(60 * scale)));
        harness.WaitForAutomationName(window, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
        Assert.Equal("dock-None-Free", window.Current.ItemStatus);
        // The pose changes scale when undocking, so its center is not the grabbed point.
        // Verify the captured logical anchor stays under the actual cursor at release.
        using var drag=System.Text.Json.JsonDocument.Parse(window.Current.HelpText);
        var capturedGrab=drag.RootElement;
        Assert.InRange(capturedGrab.GetProperty("pressX").GetInt32(),startX-1,startX+1);
        Assert.InRange(capturedGrab.GetProperty("pressY").GetInt32(),startY-1,startY+1);
        var after=harness.GetWindowRect();
        var afterScale=api.GetDpiForWindow(handle)/96d;
        Assert.InRange(after.Left+capturedGrab.GetProperty("grabXDip").GetDouble()*afterScale,startX+deltaX-3*scale,startX+deltaX+3*scale);
        Assert.InRange(after.Top+capturedGrab.GetProperty("grabYDip").GetDouble()*afterScale,startY+60*scale-3*scale,startY+60*scale+3*scale);
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
