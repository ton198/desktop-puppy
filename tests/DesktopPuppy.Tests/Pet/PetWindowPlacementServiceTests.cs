using DesktopPuppy.Native;
using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class PetWindowPlacementServiceTests
{
    private static readonly MonitorDisplayInfo Primary = new(
        "primary",
        new PhysicalRect(0, 0, 1920, 1080),
        new PhysicalRect(0, 0, 1920, 1040),
        isPrimary: true);

    // Purpose: Keep the first real pet window accessible when WPF startup placement is off-screen or under taskbar bounds.
    // Coverage: Fake HWND bounds and primary work area.
    // Target effect: Physical target rectangle matches policy and preserves the 20-pixel visible edge.
    [Fact]
    public void Initial_placement_clamps_window_inside_primary_work_area()
    {
        var hwnd = new IntPtr(1);
        var windows = new FakeWindowPositioner
        {
            Bounds = new PhysicalRect(-500, -500, -240, -290),
            Monitor = Primary,
            Dpi = 96
        };
        using var service = new PetWindowPlacementService(new FakeMonitorProvider(Primary), windows);

        service.ApplyInitial(hwnd, widthDip: 260, heightDip: 210);

        Assert.Equal(new PhysicalRect(-240, -190, 20, 20), windows.Bounds);
        Assert.Equal(1, windows.SetBoundsCalls);
    }

    // Purpose: Avoid assuming one display scale for every WPF window.
    // Coverage: Same DIP size at 100% and 150%, with actual HWND DPI supplied by the fake adapter.
    // Target effect: Physical extents use measured DPI and the visible edge remains 20 physical pixels.
    [Fact]
    public void Placement_uses_actual_window_dpi_for_physical_size()
    {
        var normalDpiHwnd = new IntPtr(1);
        var highDpiHwnd = new IntPtr(2);
        var windows = new FakeWindowPositioner();
        windows.AddWindow(normalDpiHwnd, new PhysicalRect(-1000, -1000, -900, -900), Primary, 96);
        windows.AddWindow(highDpiHwnd, new PhysicalRect(-1000, -1000, -900, -900), Primary, 144);
        using var service = new PetWindowPlacementService(new FakeMonitorProvider(Primary), windows);

        service.ApplyInitial(normalDpiHwnd, widthDip: 100, heightDip: 80);
        service.ApplyInitial(highDpiHwnd, widthDip: 100, heightDip: 80);

        var normalBounds = windows.GetBounds(normalDpiHwnd);
        var highBounds = windows.GetBounds(highDpiHwnd);
        Assert.Equal((100, 80), (normalBounds.Width, normalBounds.Height));
        Assert.Equal((150, 120), (highBounds.Width, highBounds.Height));
        Assert.Equal(20, normalBounds.Left + normalBounds.Width);
        Assert.Equal(20, normalBounds.Top + normalBounds.Height);
        Assert.Equal(20, highBounds.Left + highBounds.Width);
        Assert.Equal(20, highBounds.Top + highBounds.Height);
    }

    // Purpose: Ensure a disconnected display cannot strand the pet.
    // Coverage: Cached secondary-monitor request followed by enumeration without that display.
    // Target effect: Bounds equal the policy-clamped cached request on the primary work area.
    [Fact]
    public void Topology_change_moves_pet_from_missing_monitor_to_primary_fallback()
    {
        var secondary = new MonitorDisplayInfo(
            "secondary",
            new PhysicalRect(-1280, 0, 0, 1024),
            new PhysicalRect(-1280, 0, 0, 984),
            isPrimary: false);
        var hwnd = new IntPtr(1);
        var monitors = new FakeMonitorProvider(Primary, secondary);
        var windows = new FakeWindowPositioner
        {
            Bounds = new PhysicalRect(-1100, 100, -840, 310),
            Monitor = secondary,
            Dpi = 96
        };
        using var service = new PetWindowPlacementService(monitors, windows);
        service.ApplyInitial(hwnd, widthDip: 260, heightDip: 210);

        monitors.SetMonitors(Primary);
        windows.SetWindow(hwnd, new PhysicalRect(500, 300, 760, 510), Primary, 96);
        service.Reconcile(hwnd, widthDip: 260, heightDip: 210);

        Assert.Equal(new PhysicalRect(-240, 100, 20, 310), windows.GetBounds(hwnd));
    }

    // Purpose: Prevent move-notification feedback loops.
    // Coverage: Two reconciliations when actual bounds are already valid.
    // Target effect: No additional SetBounds call occurs.
    [Fact]
    public void Repeated_reconcile_does_not_issue_redundant_native_moves()
    {
        var hwnd = new IntPtr(1);
        var windows = new FakeWindowPositioner
        {
            Bounds = new PhysicalRect(100, 100, 360, 310),
            Monitor = Primary,
            Dpi = 96
        };
        using var service = new PetWindowPlacementService(new FakeMonitorProvider(Primary), windows);

        service.Reconcile(hwnd, widthDip: 260, heightDip: 210);
        service.Reconcile(hwnd, widthDip: 260, heightDip: 210);

        Assert.Equal(0, windows.SetBoundsCalls);
    }

    // Purpose: Preserve a deterministic recovery monitor when the prior device disappears.
    // Coverage: Unknown prior ID plus one current primary display.
    // Target effect: Placement selects the primary monitor.
    [Fact]
    public void Monitor_selection_falls_back_to_primary_when_previous_device_is_missing()
    {
        var unknownMonitor = new MonitorDisplayInfo(
            "removed-display",
            new PhysicalRect(-1920, 0, 0, 1080),
            new PhysicalRect(-1920, 0, 0, 1040),
            isPrimary: false);
        var hwnd = new IntPtr(1);
        var windows = new FakeWindowPositioner
        {
            Bounds = new PhysicalRect(3000, 1200, 3260, 1410),
            Monitor = unknownMonitor,
            Dpi = 96
        };
        using var service = new PetWindowPlacementService(new FakeMonitorProvider(Primary), windows);

        service.ApplyInitial(hwnd, widthDip: 260, heightDip: 210);

        Assert.Equal(new PhysicalRect(1900, 1020, 2160, 1230), windows.GetBounds(hwnd));
    }

    // Purpose: Prevent stale monitor identity/coordinates leaking into a later window lifetime.
    // Coverage: Apply placement, call Forget(hwnd), then start a second HWND.
    // Target effect: The new HWND uses its own geometry and does not reuse the old pet position.
    [Fact]
    public void Forget_discards_cached_state_when_pet_window_closes()
    {
        var secondary = new MonitorDisplayInfo(
            "secondary",
            new PhysicalRect(-1280, 0, 0, 1024),
            new PhysicalRect(-1280, 0, 0, 984),
            isPrimary: false);
        var hwnd = new IntPtr(1);
        var monitors = new FakeMonitorProvider(Primary, secondary);
        var windows = new FakeWindowPositioner
        {
            Bounds = new PhysicalRect(-1100, 100, -840, 310),
            Monitor = secondary,
            Dpi = 96
        };
        using var service = new PetWindowPlacementService(monitors, windows);

        service.ApplyInitial(hwnd, widthDip: 260, heightDip: 210);
        monitors.SetMonitors(Primary);
        windows.SetWindow(hwnd, new PhysicalRect(700, 500, 960, 710), Primary, 96);
        service.Forget(hwnd);
        service.ApplyInitial(hwnd, widthDip: 260, heightDip: 210);

        Assert.Equal(new PhysicalRect(700, 500, 960, 710), windows.GetBounds(hwnd));
    }

    private sealed class FakeMonitorProvider(params MonitorDisplayInfo[] monitors) : IMonitorDisplayProvider
    {
        private IReadOnlyList<MonitorDisplayInfo> _monitors = monitors;

        public event EventHandler? DisplaySettingsChanged { add { } remove { } }

        public IReadOnlyList<MonitorDisplayInfo> GetMonitors() => _monitors;

        public void SetMonitors(params MonitorDisplayInfo[] monitors) => _monitors = monitors;
    }

    private sealed class FakeWindowPositioner : INativeWindowPositioner
    {
        private readonly Dictionary<IntPtr, WindowState> _windows = new();
        private IntPtr _defaultHwnd = new(1);
        private int _setBoundsCalls;

        public PhysicalRect Bounds
        {
            get => GetBounds(_defaultHwnd);
            set
            {
                if (_windows.TryGetValue(_defaultHwnd, out var state))
                {
                    _windows[_defaultHwnd] = state with { Bounds = value };
                }
                else
                {
                    AddWindow(_defaultHwnd, value, Primary, 96);
                }
            }
        }

        public MonitorDisplayInfo Monitor
        {
            get => _windows.TryGetValue(_defaultHwnd, out var state) ? state.Monitor : Primary;
            set
            {
                if (_windows.TryGetValue(_defaultHwnd, out var state))
                {
                    _windows[_defaultHwnd] = state with { Monitor = value };
                }
                else
                {
                    AddWindow(_defaultHwnd, new PhysicalRect(0, 0, 260, 210), value, 96);
                }
            }
        }

        public uint Dpi
        {
            get => _windows.TryGetValue(_defaultHwnd, out var state) ? state.Dpi : 96;
            set
            {
                if (_windows.TryGetValue(_defaultHwnd, out var state))
                {
                    _windows[_defaultHwnd] = state with { Dpi = value };
                }
                else
                {
                    AddWindow(_defaultHwnd, new PhysicalRect(0, 0, 260, 210), Primary, value);
                }
            }
        }

        public int SetBoundsCalls => _setBoundsCalls;

        public void AddWindow(IntPtr hwnd, PhysicalRect bounds, MonitorDisplayInfo monitor, uint dpi)
        {
            _windows[hwnd] = new WindowState(bounds, monitor, dpi);
            _defaultHwnd = hwnd;
        }

        public void SetWindow(IntPtr hwnd, PhysicalRect bounds, MonitorDisplayInfo monitor, uint dpi) =>
            AddWindow(hwnd, bounds, monitor, dpi);

        public PhysicalRect GetBounds(IntPtr hwnd) => _windows[hwnd].Bounds;

        public MonitorDisplayInfo GetMonitorForWindow(IntPtr hwnd) => _windows[hwnd].Monitor;

        public uint GetDpi(IntPtr hwnd) => _windows[hwnd].Dpi;

        public void SetBounds(IntPtr hwnd, PhysicalRect bounds)
        {
            _setBoundsCalls++;
            _windows[hwnd] = _windows[hwnd] with { Bounds = bounds };
        }

        private sealed record WindowState(PhysicalRect Bounds, MonitorDisplayInfo Monitor, uint Dpi);
    }
}
