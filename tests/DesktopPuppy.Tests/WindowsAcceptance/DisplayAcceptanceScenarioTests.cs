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
            new PhysicalRect(2020, 100, 2154, 287)));
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
            new PhysicalRect(2020, 100, 2131, 256)));
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
            new PhysicalRect(2020, 100, 2154, 287)));
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
            new PhysicalRect(3825, 100, 3959, 287)));
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

    // Purpose: prove monitor-removal placement is calculated from the captured pre-removal physical request.
    // Coverage: target removal, retained primary membership, live DPI-scaled extents, primary work-area clamping, and a live app.
    // Target effect: only the captured request clamped by policy to the connected primary can pass; post-change bounds cannot redefine the expected position.
    [Fact]
    public void Removed_monitor_fallback_uses_pre_removal_request_and_primary_policy_result()
    {
        var request = new DisplayRemovalRequest(
            "Display 2", "opaque-target-device", "Display 1", "opaque-primary-device",
            new PhysicalRect(3500, 900, 3611, 1056), WorkAreaOne, PetHwnd, AppProcessId, 0);
        var primary = new DisplayAcceptanceMonitor(
            "Display 1", "opaque-primary-device", new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true);
        var actual = new DisplayAcceptanceObservation(
            "Display 2", PetHwnd, AppProcessId, 144, new PhysicalRect(1900, 900, 2034, 1087),
            "Display 1", primary.Bounds, primary.WorkArea);

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], actual, AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Passed, result.Status);
        Assert.Contains("Display 2", result.Summary);
        Assert.Contains("Display 1", result.Summary);
        Assert.DoesNotContain("opaque-", result.Summary, StringComparison.Ordinal);
    }

    // Purpose: enforce the specified one-physical-pixel tolerance on all four actual HWND edges.
    // Coverage: a fallback rectangle with each edge one pixel from the independently calculated primary-policy result.
    // Target effect: one-pixel native rounding differences pass, while larger deviations are rejected by the companion case.
    [Fact]
    public void Removed_monitor_fallback_accepts_one_pixel_edge_rounding()
    {
        var request = new DisplayRemovalRequest(
            "Display 2", "opaque-target-device", "Display 1", "opaque-primary-device",
            new PhysicalRect(3500, 900, 3611, 1056), WorkAreaOne, PetHwnd, AppProcessId, 0);
        var primary = new DisplayAcceptanceMonitor(
            "Display 1", "opaque-primary-device", new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true);
        var actual = new DisplayAcceptanceObservation(
            "Display 2", PetHwnd, AppProcessId, 144, new PhysicalRect(1899, 901, 2033, 1088),
            "Display 1", primary.Bounds, primary.WorkArea);

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], actual, AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Passed, result.Status);
    }

    // Purpose: reject a primary fallback whose HWND edge is more than one physical pixel from policy.
    // Coverage: otherwise-valid recovery geometry with a two-pixel deviation and at least 20 pixels visible.
    // Target effect: visible bounds outside the one-pixel tolerance cannot pass.
    [Fact]
    public void Removed_monitor_fallback_rejects_two_pixel_edge_error()
    {
        var request = CreateRemovalRequest();
        var primary = CreatePrimaryMonitor();
        var actual = CreateFallbackObservation(primary) with { Bounds = new PhysicalRect(1898, 900, 2032, 1087) };

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], actual, AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("more than 1 physical pixel", result.Summary);
    }

    // Purpose: reject a topology observation when the removed target still appears in refreshed enumeration.
    // Coverage: otherwise-correct live primary fallback while the target device remains enumerated.
    // Target effect: target absence is mandatory before fallback recovery can pass.
    [Fact]
    public void Removed_monitor_fallback_rejects_target_still_enumerated()
    {
        var request = CreateRemovalRequest();
        var primary = CreatePrimaryMonitor();
        var target = new DisplayAcceptanceMonitor(
            "Display 2", "opaque-target-device", new PhysicalRect(1920, 0, 3840, 1080), WorkAreaTwo, false);

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary, target], CreateFallbackObservation(primary), AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("still present", result.Summary);
    }

    // Purpose: require Native monitor membership on the captured primary rather than any visible destination.
    // Coverage: valid fallback rectangle and live process reported on a non-primary run-local display alias.
    // Target effect: the actual HWND must be contained by the captured, still-primary display.
    [Fact]
    public void Removed_monitor_fallback_rejects_non_primary_membership()
    {
        var request = CreateRemovalRequest();
        var primary = CreatePrimaryMonitor();

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], CreateFallbackObservation(primary) with { MonitorAlias = "Display 3" },
            AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("captured primary", result.Summary);
    }

    // Purpose: require the isolated product process to remain alive through topology recovery.
    // Coverage: otherwise-correct primary fallback geometry and membership with the child process stopped.
    // Target effect: a stopped app cannot pass even when its last bounds happen to match.
    [Fact]
    public void Removed_monitor_fallback_rejects_stopped_app()
    {
        var request = CreateRemovalRequest();
        var primary = CreatePrimaryMonitor();

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], CreateFallbackObservation(primary), AppProcessId, appIsRunning: false);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("not running", result.Summary);
    }

    // Purpose: enforce the minimum visible edge independently from the one-pixel rectangle tolerance.
    // Coverage: a rectangle within one pixel of expected whose right edge leaves only 19 physical pixels visible.
    // Target effect: all physical edges within tolerance still fail if fewer than 20 physical pixels remain visible.
    [Fact]
    public void Removed_monitor_fallback_rejects_fewer_than_twenty_visible_pixels()
    {
        var request = CreateRemovalRequest();
        var primary = CreatePrimaryMonitor();
        var actual = CreateFallbackObservation(primary) with { Bounds = new PhysicalRect(1901, 900, 2035, 1087) };

        var result = DisplayAcceptanceScenario.ValidateRemovalRecovery(
            request, [primary], actual, AppProcessId, appIsRunning: true);

        Assert.Equal(AcceptanceStatus.Failed, result.Status);
        Assert.Contains("fewer than 20", result.Summary);
    }

    // Purpose: require the real display-change event and fresh monitor enumeration before testing fallback placement.
    // Coverage: captured Display 2 request, target disappearance, same HWND moving to the refreshed primary, live DPI, and bounded settling.
    // Target effect: the complete event-driven primary fallback passes only when its bounds derive from the captured pre-removal request.
    [Fact]
    public async Task Monitor_removal_waits_for_display_event_refreshes_topology_and_checks_fallback()
    {
        var api = CreateApi();
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(3500, 900, 3611, 1056)));
        var scenario = new DisplayAcceptanceScenario(api);
        var request = await scenario.CaptureRemovalRequestAsync("Display 2", PetHwnd, AppProcessId,
            () => true, CancellationToken.None);
        var recovery = scenario.RunRemovalRecoveryAsync(request, () => true, CancellationToken.None,
            TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        Assert.False(recovery.IsCompleted);

        api.SetMonitors([new NativeDisplaySnapshot(
            MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true)]);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorOne, "PetWindow", 144,
            new PhysicalRect(1900, 900, 2034, 1087)));
        api.RaiseDisplaySettingsChanged();

        var result = await recovery.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AcceptanceStatus.Passed, result.Status);
        Assert.Contains("captured pre-removal bounds", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, api.EnumerationCount);
        Assert.DoesNotContain("opaque-", result.Summary, StringComparison.Ordinal);
        scenario.Dispose();
    }

    // Purpose: retry transient Native enumeration failure after the actual topology-change event without requiring another event.
    // Coverage: first post-event refresh throws once, then refreshed enumeration omits the target while retaining primary membership.
    // Target effect: a single real event remains actionable through the deadline and the valid fallback proceeds to Passed.
    [Fact]
    public async Task Monitor_removal_retries_transient_enumeration_failure_after_same_event()
    {
        var api = CreateApi();
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(3500, 900, 3611, 1056)));
        var scenario = new DisplayAcceptanceScenario(api);
        var request = await scenario.CaptureRemovalRequestAsync("Display 2", PetHwnd, AppProcessId,
            () => true, CancellationToken.None);
        api.SetMonitors([new NativeDisplaySnapshot(
            MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true)]);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorOne, "PetWindow", 144,
            new PhysicalRect(1900, 900, 2034, 1087)));
        api.FailNextEnumerations(1);
        api.RaiseDisplaySettingsChanged();

        var result = await scenario.RunRemovalRecoveryAsync(request, () => true, CancellationToken.None,
            TimeSpan.FromSeconds(2));

        Assert.Equal(AcceptanceStatus.Passed, result.Status);
        Assert.True(api.EnumerationCount >= 3);
        scenario.Dispose();
    }

    // Purpose: keep anonymous run-local aliases bound to the same monitor after a topology refresh.
    // Coverage: three initial displays, removal of Display 2, and a reordered fresh enumeration for Displays 1 and 3.
    // Target effect: a remaining monitor never inherits a removed monitor's report alias during topology recovery.
    [Fact]
    public void Refreshed_monitor_aliases_remain_bound_to_their_run_local_monitor()
    {
        var thirdDevice = "opaque-third-device";
        var api = new FakeDisplayAcceptanceNativeApi(
        [
            new NativeDisplaySnapshot(MonitorTwo, new PhysicalRect(1920, 0, 3840, 1080), WorkAreaTwo, false),
            new NativeDisplaySnapshot(thirdDevice, new PhysicalRect(3840, 0, 5760, 1080),
                new PhysicalRect(3840, 0, 5760, 1040), false),
            new NativeDisplaySnapshot(MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true)
        ]);
        var scenario = new DisplayAcceptanceScenario(api);
        var initial = scenario.GetMonitors();
        api.SetMonitors(
        [
            new NativeDisplaySnapshot(thirdDevice, new PhysicalRect(3840, 0, 5760, 1080),
                new PhysicalRect(3840, 0, 5760, 1040), false),
            new NativeDisplaySnapshot(MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true)
        ]);

        var refreshed = scenario.RefreshMonitors();

        Assert.Equal("Display 1", initial.Single(item => item.DeviceId == MonitorOne).Alias);
        Assert.Equal("Display 2", initial.Single(item => item.DeviceId == MonitorTwo).Alias);
        Assert.Equal("Display 3", initial.Single(item => item.DeviceId == thirdDevice).Alias);
        Assert.Equal("Display 1", refreshed.Single(item => item.DeviceId == MonitorOne).Alias);
        Assert.Equal("Display 3", refreshed.Single(item => item.DeviceId == thirdDevice).Alias);
        scenario.Dispose();
    }

    // Purpose: require an actual display-change event even if a fresh-looking topology would omit the target.
    // Coverage: target disappears in the fake topology, but no post-capture DisplaySettingsChanged event arrives before a short deadline.
    // Target effect: enumeration alone cannot pass; the missing actual event yields Incomplete.
    [Fact]
    public async Task Monitor_removal_timeout_without_real_event_is_incomplete()
    {
        var api = CreateApi();
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(3500, 900, 3611, 1056)));
        var scenario = new DisplayAcceptanceScenario(api);
        var request = await scenario.CaptureRemovalRequestAsync("Display 2", PetHwnd, AppProcessId,
            () => true, CancellationToken.None);
        api.SetMonitors([new NativeDisplaySnapshot(
            MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true)]);

        var result = await scenario.RunRemovalRecoveryAsync(request, () => true, CancellationToken.None,
            TimeSpan.FromMilliseconds(40));

        Assert.Equal(AcceptanceStatus.Incomplete, result.Status);
        Assert.Contains("Timed out", result.Summary, StringComparison.OrdinalIgnoreCase);
        scenario.Dispose();
    }

    // Purpose: make operator cancellation non-passing during the manual topology wait.
    // Coverage: cancellation after a valid pre-removal capture and before any real display-change event.
    // Target effect: cancellation returns Incomplete without requiring a topology change or creating a pass result.
    [Fact]
    public async Task Monitor_removal_cancellation_is_incomplete()
    {
        var api = CreateApi();
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorTwo, "PetWindow", 120,
            new PhysicalRect(3500, 900, 3611, 1056)));
        var scenario = new DisplayAcceptanceScenario(api);
        var request = await scenario.CaptureRemovalRequestAsync("Display 2", PetHwnd, AppProcessId,
            () => true, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await scenario.RunRemovalRecoveryAsync(request, () => true, cancellation.Token,
            TimeSpan.FromSeconds(1));

        Assert.Equal(AcceptanceStatus.Incomplete, result.Status);
        Assert.Contains("cancelled", result.Summary, StringComparison.OrdinalIgnoreCase);
        scenario.Dispose();
    }

    private static DisplayRemovalRequest CreateRemovalRequest() => new(
        "Display 2", "opaque-target-device", "Display 1", "opaque-primary-device",
        new PhysicalRect(3500, 900, 3634, 1087), WorkAreaOne, PetHwnd, AppProcessId, 0);

    private static DisplayAcceptanceMonitor CreatePrimaryMonitor() => new(
        "Display 1", "opaque-primary-device", new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true);

    private static DisplayAcceptanceObservation CreateFallbackObservation(DisplayAcceptanceMonitor primary) => new(
        "Display 1", PetHwnd, AppProcessId, 144, new PhysicalRect(1900, 900, 2034, 1087),
        primary.Alias, primary.Bounds, primary.WorkArea);

    private static FakeDisplayAcceptanceNativeApi CreateApi()
    {
        var api = new FakeDisplayAcceptanceNativeApi(
        [
            new NativeDisplaySnapshot(MonitorOne, new PhysicalRect(0, 0, 1920, 1080), WorkAreaOne, true),
            new NativeDisplaySnapshot(MonitorTwo, new PhysicalRect(1920, 0, 3840, 1080), WorkAreaTwo, false)
        ]);
        api.SetWindow(PetHwnd, new FakeWindowState(AppProcessId, MonitorOne, "PetWindow", 120,
            new PhysicalRect(100, 100, 211, 256)));
        return api;
    }

    private sealed class FakeDisplayAcceptanceNativeApi(IReadOnlyList<NativeDisplaySnapshot> monitors)
        : IDisplayAcceptanceNativeApi
    {
        private readonly Dictionary<IntPtr, FakeWindowState> windows = new();
        private IReadOnlyList<NativeDisplaySnapshot> currentMonitors = monitors;
        public event EventHandler? DisplaySettingsChanged;
        public int EnumerationCount { get; private set; }
        private int enumerationFailuresRemaining;

        public void SetWindow(IntPtr hwnd, FakeWindowState state) => windows[hwnd] = state;
        public void FailNextEnumerations(int count) => enumerationFailuresRemaining = count;
        public void SetMonitors(IReadOnlyList<NativeDisplaySnapshot> snapshots) => currentMonitors = snapshots;
        public void RaiseDisplaySettingsChanged() => DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
        public IntPtr FindPetWindowHandle(uint processId) => windows
            .Where(pair => pair.Value.ProcessId == processId && pair.Value.AutomationId == "PetWindow")
            .Select(pair => pair.Key)
            .FirstOrDefault();
        public IReadOnlyList<NativeDisplaySnapshot> EnumerateMonitors()
        {
            EnumerationCount++;
            if (enumerationFailuresRemaining > 0)
            {
                enumerationFailuresRemaining--;
                throw new InvalidOperationException("Temporary topology enumeration failure.");
            }
            return currentMonitors;
        }
        public bool IsPetWindow(IntPtr hwnd) => windows.TryGetValue(hwnd, out var state) && state.AutomationId == "PetWindow";
        public uint GetWindowProcessId(IntPtr hwnd) => windows[hwnd].ProcessId;
        public PhysicalRect GetWindowBounds(IntPtr hwnd) => windows[hwnd].Bounds;
        public NativeDisplaySnapshot GetMonitorForWindow(IntPtr hwnd) =>
            currentMonitors.Single(monitor => monitor.DeviceId == windows[hwnd].MonitorId);
        public uint GetDpiForWindow(IntPtr hwnd) => windows[hwnd].Dpi;
    }

    private sealed record FakeWindowState(uint ProcessId, string MonitorId, string AutomationId, uint Dpi, PhysicalRect Bounds);
}
