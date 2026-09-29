using DesktopPuppy.Native;
using DesktopPuppy.WindowsAcceptance;
using DesktopPuppy.WindowsAcceptance.Core;
using Xunit;

namespace DesktopPuppy.Tests.WindowsAcceptance;

public sealed class DisplayAcceptanceScenarioTests
{
    private const uint AppProcessId = 4321;
    private static readonly IntPtr PetHwnd = new(101);
    private const string MonitorOne = "run-monitor-one";
    private const string MonitorTwo = "run-monitor-two";
    private static readonly PhysicalRect WorkAreaOne = new(0, 0, 1920, 1040);
    private static readonly PhysicalRect WorkAreaTwo = new(1920, 0, 3840, 1040);

    // Purpose: prevent display hardware access when either opt-in or local-session safety gate is unmet.
    // Coverage: both opt-in flags, interactive/remote-session checks, and monitor enumeration call ordering.
    // Target effect: unsafe or non-opted-in runs return a skip reason before monitor discovery can proceed.
    [Theory]
    [InlineData(null, "1", true, 0, "Opt-in is not enabled")]
    [InlineData("1", null, true, 0, "DESKTOP_PUPPY_DISPLAY_ACCEPTANCE must be 1")]
    [InlineData("1", "1", false, 0, "local interactive Windows session")]
    [InlineData("1", "1", true, 1, "local interactive Windows session")]
    public void Prerequisite_gate_skips_before_enumerating_monitors(
        string? acceptanceOptIn, string? displayOptIn, bool userInteractive, int remoteMetric, string reason)
    {
        var enumerations = 0;

        var actual = DisplayAcceptanceScenario.PrerequisiteSkipReason(
            acceptanceOptIn,
            displayOptIn,
            userInteractive,
            remoteMetric,
            () => { enumerations++; return 2; });

        Assert.NotNull(actual);
        Assert.Contains(reason, actual, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, enumerations);
    }

    // Purpose: prevent a single-display machine from starting an isolated app or run database.
    // Coverage: fully opted-in local session with one and two enumerated displays.
    // Target effect: fewer than two displays is skipped while two displays pass the display-specific gate.
    [Fact]
    public void Display_count_gate_requires_two_enumerated_monitors()
    {
        Assert.Contains("at least two", DisplayAcceptanceScenario.PrerequisiteSkipReason(
            "1", "1", true, 0, () => 1)!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(DisplayAcceptanceScenario.PrerequisiteSkipReason(
            "1", "1", true, 0, () => 2));
    }

    // Purpose: verify the real acceptance validator using fake read-only native observations.
    // Coverage: one HWND manually observed on two displays at distinct DPI values, physical size, work areas, and live process.
    // Target effect: policy-compliant bounds on both requested displays with at least 20 visible pixels return Passed.
    [Fact]
    public void Distinct_dpi_observations_for_same_live_pet_pass_when_policy_compliant()
    {
        var api = CreateApi();
        var scenario = new DisplayAcceptanceScenario(api);
        var monitors = scenario.GetMonitors();
        var first = scenario.Observe("Display 1", PetHwnd, AppProcessId, appIsRunning: true);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 144,
            new PhysicalRect(2020, 100, 2410, 415)));
        var second = scenario.Observe("Display 2", PetHwnd, AppProcessId, appIsRunning: true);

        var result = DisplayAcceptanceScenario.Validate(monitors, [first, second], AppProcessId);

        Assert.Equal(AcceptanceStatus.Passed, result.Status);
        Assert.Contains("Display 1", result.Summary);
        Assert.Contains("Display 2", result.Summary);
        Assert.Contains("120 DPI", result.Summary);
        Assert.Contains("144 DPI", result.Summary);
    }

    // Purpose: distinguish unsupported equal-DPI hardware from a successful mixed-DPI result.
    // Coverage: same live pet HWND on two requested displays with equal measured GetDpiForWindow values.
    // Target effect: the result is Skipped and states the measured equal DPI values instead of passing.
    [Fact]
    public void Equal_measured_dpi_values_return_skipped_with_measured_reason()
    {
        var api = CreateApi();
        var scenario = new DisplayAcceptanceScenario(api);
        var monitors = scenario.GetMonitors();
        var first = scenario.Observe("Display 1", PetHwnd, AppProcessId, appIsRunning: true);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(2020, 100, 2345, 363)));
        var second = scenario.Observe("Display 2", PetHwnd, AppProcessId, appIsRunning: true);

        var result = DisplayAcceptanceScenario.Validate(monitors, [first, second], AppProcessId);

        Assert.Equal(AcceptanceStatus.Skipped, result.Status);
        Assert.Contains("equal", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("120 DPI", result.Summary);
    }

    // Purpose: make equal measured DPI an environment skip even if the remaining mixed-DPI geometry cannot be judged.
    // Coverage: same HWND is confirmed on both requested displays, but the pair reports an invalid second physical size.
    // Target effect: equal nonzero DPI values always produce Skipped with both display aliases and the measured value.
    [Fact]
    public void Equal_dpi_pair_is_skipped_before_mixed_dpi_geometry_assertions()
    {
        var api = CreateApi();
        var scenario = new DisplayAcceptanceScenario(api);
        var monitors = scenario.GetMonitors();
        var first = scenario.Observe("Display 1", PetHwnd, AppProcessId, appIsRunning: true);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(2020, 100, 2300, 363)));
        var second = scenario.Observe("Display 2", PetHwnd, AppProcessId, appIsRunning: true);

        var result = DisplayAcceptanceScenario.Validate(monitors, [first, second], AppProcessId);

        Assert.Equal(AcceptanceStatus.Skipped, result.Status);
        Assert.Contains("Display 1", result.Summary);
        Assert.Contains("Display 2", result.Summary);
        Assert.Contains("120 DPI", result.Summary);
    }

    // Purpose: ensure observations cannot combine different windows or report a pet on the wrong target display.
    // Coverage: changed HWND and containing-monitor mismatch across operator-confirmed observations.
    // Target effect: either identity/target mismatch returns Failed and cannot satisfy the scenario.
    [Fact]
    public void Different_hwnd_or_wrong_containing_monitor_fails_validation()
    {
        var scenario = new DisplayAcceptanceScenario(CreateApi());
        var monitors = scenario.GetMonitors();
        var first = scenario.Observe("Display 1", PetHwnd, AppProcessId, appIsRunning: true);
        var api = CreateApi();
        var secondScenario = new DisplayAcceptanceScenario(api);
        var differentHwnd = new IntPtr(102);
        api.SetWindow(differentHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 144,
            new PhysicalRect(2020, 100, 2410, 415)));
        var second = secondScenario.Observe("Display 2", differentHwnd, AppProcessId, appIsRunning: true);
        var result = DisplayAcceptanceScenario.Validate(monitors, [first, second], AppProcessId);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("same pet HWND", result.Summary, StringComparison.OrdinalIgnoreCase);

        var wrongTarget = second with { Hwnd = PetHwnd, TargetAlias = "Display 1" };
        result = DisplayAcceptanceScenario.Validate(monitors, [first, wrongTarget], AppProcessId);
        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("requested display", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: reject geometry that violates the production physical placement policy or visible-pixel minimum.
    // Coverage: an otherwise valid mixed-DPI pair with the second window placed beyond the legal work-area edge.
    // Target effect: a window with less than the policy's 20-pixel visible margin cannot pass.
    [Fact]
    public void Bounds_outside_policy_visible_margin_fail_validation()
    {
        var api = CreateApi();
        var scenario = new DisplayAcceptanceScenario(api);
        var monitors = scenario.GetMonitors();
        var first = scenario.Observe("Display 1", PetHwnd, AppProcessId, appIsRunning: true);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 144,
            new PhysicalRect(3825, 100, 4215, 415)));
        var second = scenario.Observe("Display 2", PetHwnd, AppProcessId, appIsRunning: true);

        var result = DisplayAcceptanceScenario.Validate(monitors, [first, second], AppProcessId);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("placement policy", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: bind the live observation flow to the isolated app's UIA pet window rather than titles or unrelated windows.
    // Coverage: fake UIA discovery returns the pet HWND owned by the expected app PID.
    // Target effect: the operator receives one stable pet HWND to manually move and observe on both displays.
    [Fact]
    public async Task Wait_for_pet_window_returns_only_the_isolated_process_pet_handle()
    {
        var api = CreateApi();
        var scenario = new DisplayAcceptanceScenario(api);

        var actual = await scenario.WaitForPetWindowHandleAsync(
            AppProcessId, appIsRunning: () => true, CancellationToken.None);

        Assert.Equal(PetHwnd, actual);
    }

    // Purpose: retain isolated data if an owned app cannot be stopped while always disposing the display provider.
    // Coverage: stop-task failure, unconfirmed process exit, confirmed process exit, and provider disposal.
    // Target effect: deletion occurs only after confirmed exit; scenario/provider disposal always runs.
    [Fact]
    public async Task Failed_display_child_cleanup_retains_run_directory_and_disposes_provider()
    {
        var deleted = false;
        var disposed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DisplayAcceptanceRunCleanup.CleanupAsync(
            () => Task.FromException<bool>(new InvalidOperationException("stop failed")),
            () => deleted = true,
            () => disposed = true));
        Assert.False(deleted);
        Assert.True(disposed);

        deleted = false;
        disposed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DisplayAcceptanceRunCleanup.CleanupAsync(
            () => Task.FromResult(false),
            () => deleted = true,
            () => disposed = true));
        Assert.False(deleted);
        Assert.True(disposed);

        deleted = false;
        disposed = false;
        await DisplayAcceptanceRunCleanup.CleanupAsync(
            () => Task.FromResult(true),
            () => deleted = true,
            () => disposed = true);
        Assert.True(deleted);
        Assert.True(disposed);
    }

    private static FakeDisplayAcceptanceNativeApi CreateApi()
    {
        var api = new FakeDisplayAcceptanceNativeApi(
        [
            new NativeDisplaySnapshot(MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true),
            new NativeDisplaySnapshot(MonitorTwo, new PhysicalRect(1920, 0, 3840, 1080), WorkAreaTwo, false)
        ]);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorOne, "PetWindow", 120,
            new PhysicalRect(100, 100, 425, 363)));
        return api;
    }

    private sealed class FakeDisplayAcceptanceNativeApi(IReadOnlyList<NativeDisplaySnapshot> monitors)
        : IDisplayAcceptanceNativeApi
    {
        private readonly Dictionary<IntPtr, FakeWindowState> windows = new();

        public void SetWindow(IntPtr hwnd, FakeWindowState state) => windows[hwnd] = state;
        public IntPtr FindPetWindowHandle(uint processId) => windows
            .Where(pair => pair.Value.ProcessId == processId && pair.Value.AutomationId == "PetWindow")
            .Select(pair => pair.Key)
            .FirstOrDefault();
        public IReadOnlyList<NativeDisplaySnapshot> EnumerateMonitors() => monitors;
        public bool IsPetWindow(IntPtr hwnd) => windows.TryGetValue(hwnd, out var state) && state.AutomationId == "PetWindow";
        public uint GetWindowProcessId(IntPtr hwnd) => windows[hwnd].ProcessId;
        public PhysicalRect GetWindowBounds(IntPtr hwnd) => windows[hwnd].Bounds;
        public NativeDisplaySnapshot GetMonitorForWindow(IntPtr hwnd) =>
            monitors.Single(monitor => monitor.DeviceId == windows[hwnd].MonitorId);
        public uint GetDpiForWindow(IntPtr hwnd) => windows[hwnd].Dpi;
    }

    private sealed record FakeWindowState(uint ProcessId, string MonitorId, string AutomationId, uint Dpi, PhysicalRect Bounds);
}
