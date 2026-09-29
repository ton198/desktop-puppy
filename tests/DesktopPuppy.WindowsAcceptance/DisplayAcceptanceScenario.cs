using System.Runtime.InteropServices;
using System.Windows.Automation;
using DesktopPuppy.Native;
using DesktopPuppy.Pet.Placement;
using DesktopPuppy.WindowsAcceptance.Core;

namespace DesktopPuppy.WindowsAcceptance;

internal sealed class DisplayAcceptanceScenario : IDisposable
{
    private const string PetWindowAutomationId = "PetWindow";
    private const double PetWidthDip = 260;
    private const double PetHeightDip = 210;

    private readonly IDisplayAcceptanceNativeApi native;
    private readonly Dictionary<string, string> aliasesByDeviceId = new(StringComparer.Ordinal);
    private IReadOnlyList<DisplayAcceptanceMonitor>? monitors;
    private int nextDisplayAliasNumber = 1;
    private readonly SemaphoreSlim displayChangeSignal = new(0, 1);
    private int displayChangeGeneration;
    private bool disposed;

    internal DisplayAcceptanceScenario(IDisplayAcceptanceNativeApi native)
    {
        this.native = native ?? throw new ArgumentNullException(nameof(native));
        this.native.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        native.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        displayChangeSignal.Dispose();
        if (native is IDisposable disposable) disposable.Dispose();
    }

    internal IReadOnlyList<DisplayAcceptanceMonitor> GetMonitors()
    {
        if (monitors is not null) return monitors;
        return RefreshMonitors();
    }

    internal IReadOnlyList<DisplayAcceptanceMonitor> RefreshMonitors()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var enumerated = native.EnumerateMonitors()
            .Select((monitor, index) => (monitor, index))
            .OrderByDescending(item => item.monitor.IsPrimary)
            .ThenBy(item => item.index)
            .ToArray();
        var refreshed = new List<DisplayAcceptanceMonitor>(enumerated.Length);
        foreach (var (monitor, _) in enumerated)
        {
            if (!aliasesByDeviceId.TryGetValue(monitor.DeviceId, out var alias))
            {
                alias = $"Display {nextDisplayAliasNumber++}";
                aliasesByDeviceId.Add(monitor.DeviceId, alias);
            }
            refreshed.Add(new DisplayAcceptanceMonitor(
                alias, monitor.DeviceId, monitor.Bounds, monitor.WorkArea, monitor.IsPrimary));
        }
        monitors = refreshed.AsReadOnly();
        return monitors;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref displayChangeGeneration);
        try { displayChangeSignal.Release(); }
        catch (SemaphoreFullException) { /* Coalesce notifications; the generation preserves event ordering. */ }
        catch (ObjectDisposedException) { /* Disposal is safe if a callback was already in flight. */ }
    }

    internal async Task<DisplayRemovalRequest> CaptureRemovalRequestAsync(
        string targetAlias,
        IntPtr hwnd,
        uint expectedProcessId,
        Func<bool> appIsRunning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appIsRunning);
        if (!appIsRunning())
            throw new DisplayRemovalPreconditionException("The isolated Desktop Puppy process exited before the removal request was captured.");

        var initialMonitors = RefreshMonitors();
        if (initialMonitors.Count < 2)
            throw new DisplayRemovalPreconditionException("At least two connected displays are required before the removal request can be captured.");
        var initialTarget = initialMonitors.FirstOrDefault(item => item.Alias == targetAlias)
            ?? throw new DisplayRemovalPreconditionException("The requested run-local removal display is unavailable.");
        var initialPrimary = initialMonitors.SingleOrDefault(item => item.IsPrimary);
        if (initialPrimary is null || initialTarget.IsPrimary || initialTarget.DeviceId == initialPrimary.DeviceId)
            throw new DisplayRemovalPreconditionException("Display 2 must be a connected secondary display for this scenario.");

        var observation = await ObserveWhenSettledAsync(
            targetAlias, hwnd, expectedProcessId, appIsRunning, cancellationToken);
        if (observation.MonitorAlias != targetAlias)
            throw new DisplayRemovalPreconditionException("Manually move the isolated pet onto Display 2 before capturing its removal request.");

        // Read the generation before the final fresh snapshot: a concurrent topology change is either
        // rejected by that snapshot or remains newer than this captured generation for the waiter.
        var eventGeneration = Volatile.Read(ref displayChangeGeneration);
        var refreshedMonitors = RefreshMonitors();
        var target = refreshedMonitors.FirstOrDefault(item =>
            string.Equals(item.DeviceId, initialTarget.DeviceId, StringComparison.Ordinal));
        var primary = refreshedMonitors.SingleOrDefault(item =>
            item.IsPrimary && string.Equals(item.DeviceId, initialPrimary.DeviceId, StringComparison.Ordinal));
        if (target is null || primary is null || refreshedMonitors.Count < 2)
            throw new DisplayRemovalPreconditionException("The display topology changed before the pre-removal request was captured; select the scenario again.");

        return new DisplayRemovalRequest(
            target.Alias, target.DeviceId, primary.Alias, primary.DeviceId,
            observation.Bounds, primary.WorkArea, hwnd, expectedProcessId, eventGeneration);
    }

    internal async Task<AcceptanceResult> RunRemovalRecoveryAsync(
        DisplayRemovalRequest request,
        Func<bool> appIsRunning,
        CancellationToken cancellationToken,
        TimeSpan? recoveryTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(appIsRunning);
        ObjectDisposedException.ThrowIf(disposed, this);
        var timeout = recoveryTimeout ?? TimeSpan.FromMinutes(5);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(recoveryTimeout));
        var deadline = DateTime.UtcNow + timeout;
        var observedGeneration = request.DisplayChangeGeneration;
        var displayChangeObserved = false;
        IReadOnlyList<DisplayAcceptanceMonitor>? currentMonitors = null;

        try
        {
            while (currentMonitors is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!appIsRunning())
                    return Result(AcceptanceStatus.Failed, "The isolated Desktop Puppy process exited before topology recovery completed.");

                var latestGeneration = Volatile.Read(ref displayChangeGeneration);
                if (latestGeneration > observedGeneration)
                {
                    observedGeneration = latestGeneration;
                    displayChangeObserved = true;
                }

                if (displayChangeObserved)
                {
                    try
                    {
                        var refreshed = RefreshMonitors();
                        var targetStillConnected = refreshed.Any(item =>
                            string.Equals(item.DeviceId, request.TargetDeviceId, StringComparison.Ordinal));
                        var primaryStillConnected = refreshed.Any(item =>
                            item.IsPrimary && string.Equals(item.DeviceId, request.PrimaryDeviceId, StringComparison.Ordinal));
                        if (!targetStillConnected)
                        {
                            if (!primaryStillConnected)
                                return Result(AcceptanceStatus.Failed,
                                    "The captured primary display did not remain connected and primary after the target was removed.");
                            currentMonitors = refreshed;
                        }
                    }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or
                        InvalidOperationException or ArgumentException)
                    {
                        // Keep the observed-event state and retry fresh enumeration until the bounded deadline.
                    }
                }

                if (currentMonitors is not null) break;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return Result(AcceptanceStatus.Incomplete,
                        $"Timed out after {timeout.TotalMinutes:0.##} minutes waiting for a real display-change notification and fresh enumeration without {request.TargetAlias}; no acceptance verdict was produced.");

                var wait = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
                await displayChangeSignal.WaitAsync(wait, cancellationToken);
            }

            var settleDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            DisplayAcceptanceObservation? previous = null;
            DisplayAcceptanceObservation? settled = null;
            var stableSamples = 0;
            while (DateTime.UtcNow < settleDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!appIsRunning())
                    return Result(AcceptanceStatus.Failed, "The isolated Desktop Puppy process exited during primary-display fallback.");

                try
                {
                    var current = Observe(request.PrimaryAlias, request.Hwnd, request.ProcessId, appIsRunning());
                    stableSamples = current == previous ? stableSamples + 1 : 1;
                    if (stableSamples >= 3)
                    {
                        settled = current;
                        break;
                    }
                    previous = current;
                }
                catch (Exception exception) when (DateTime.UtcNow < settleDeadline &&
                    exception is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
                {
                    previous = null;
                    stableSamples = 0;
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    continue;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            if (settled is null)
                return Result(AcceptanceStatus.Incomplete,
                    "The actual pet HWND did not settle after the display-change notification; no acceptance verdict was produced.");

            return ValidateRemovalRecovery(request, currentMonitors, settled, request.ProcessId, appIsRunning());
        }
        catch (OperationCanceledException)
        {
            return Result(AcceptanceStatus.Incomplete,
                "Operator cancelled while waiting for display removal recovery; no acceptance verdict was produced.");
        }
    }

    internal string? CheckPrerequisites(
        string? windowsAcceptanceOptIn,
        string? displayAcceptanceOptIn,
        bool userInteractive,
        int remoteSessionMetric)
    {
        monitors = null;
        return PrerequisiteSkipReason(
            windowsAcceptanceOptIn,
            displayAcceptanceOptIn,
            userInteractive,
            remoteSessionMetric,
            () => GetMonitors().Count);
    }

    internal async Task<IntPtr> WaitForPetWindowHandleAsync(
        uint expectedProcessId,
        Func<bool> appIsRunning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appIsRunning);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!appIsRunning())
                throw new InvalidOperationException("The isolated Desktop Puppy process exited before its pet window appeared.");

            var hwnd = native.FindPetWindowHandle(expectedProcessId);
            if (hwnd != IntPtr.Zero && native.IsPetWindow(hwnd) &&
                native.GetWindowProcessId(hwnd) == expectedProcessId)
                return hwnd;

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException("The isolated pet window was not found through UI Automation within 15 seconds.");
    }

    internal DisplayAcceptanceObservation Observe(
        string targetAlias,
        IntPtr hwnd,
        uint expectedProcessId,
        bool appIsRunning)
    {
        if (!appIsRunning)
            throw new InvalidOperationException("The isolated Desktop Puppy process exited before the display observation.");
        if (hwnd == IntPtr.Zero || !native.IsPetWindow(hwnd))
            throw new InvalidOperationException("The isolated pet window is no longer available through UI Automation.");

        var processId = native.GetWindowProcessId(hwnd);
        if (processId != expectedProcessId)
            throw new InvalidOperationException("The observed pet window no longer belongs to the isolated app process.");

        var dpi = native.GetDpiForWindow(hwnd);
        var bounds = native.GetWindowBounds(hwnd);
        var windowMonitor = native.GetMonitorForWindow(hwnd);
        if (dpi == 0 || !bounds.IsValid || string.IsNullOrWhiteSpace(windowMonitor.DeviceId))
            throw new InvalidOperationException("The pet window's read-only DPI or display geometry could not be measured.");

        var monitor = GetMonitors().FirstOrDefault(item =>
                string.Equals(item.DeviceId, windowMonitor.DeviceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The pet window is not on a currently enumerated display.");
        if (!GetMonitors().Any(item => item.Alias == targetAlias))
            throw new InvalidOperationException("The requested run-local display alias is unavailable.");

        return new DisplayAcceptanceObservation(
            targetAlias,
            hwnd,
            processId,
            dpi,
            bounds,
            monitor.Alias,
            windowMonitor.Bounds,
            windowMonitor.WorkArea);
    }

    internal async Task<DisplayAcceptanceObservation> ObserveWhenSettledAsync(
        string targetAlias,
        IntPtr hwnd,
        uint expectedProcessId,
        Func<bool> appIsRunning,
        CancellationToken cancellationToken)
    {
        const int requiredStableSamples = 3;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        DisplayAcceptanceObservation? previous = null;
        var stableSamples = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = Observe(targetAlias, hwnd, expectedProcessId, appIsRunning());
            stableSamples = current == previous ? stableSamples + 1 : 1;
            if (stableSamples >= requiredStableSamples) return current;
            previous = current;
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException("The pet window did not settle after the manual display move within 15 seconds.");
    }

    internal static string? PrerequisiteSkipReason(
        string? windowsAcceptanceOptIn,
        string? displayAcceptanceOptIn,
        bool userInteractive,
        int remoteSessionMetric,
        Func<int> enumerateMonitorCount)
    {
        ArgumentNullException.ThrowIfNull(enumerateMonitorCount);
        if (windowsAcceptanceOptIn != "1")
            return "Opt-in is not enabled (DESKTOP_PUPPY_WINDOWS_ACCEPTANCE must be 1).";
        if (displayAcceptanceOptIn != "1")
            return "Display acceptance opt-in is not enabled (DESKTOP_PUPPY_DISPLAY_ACCEPTANCE must be 1).";
        if (!userInteractive || remoteSessionMetric != 0)
            return "A local interactive Windows session is required.";

        try
        {
            if (enumerateMonitorCount() < 2)
                return "At least two connected displays are required; the scenario was not started.";
        }
        catch (Exception)
        {
            return "Connected displays could not be safely enumerated; the scenario was not started.";
        }

        return null;
    }

    internal static AcceptanceResult Validate(
        IReadOnlyList<DisplayAcceptanceMonitor> availableMonitors,
        IReadOnlyList<DisplayAcceptanceObservation> observations,
        uint expectedProcessId)
    {
        if (availableMonitors.Count < 2)
            return Result(AcceptanceStatus.Failed, "Fewer than two displays were available for validation.");
        if (observations.Count != 2)
            return Result(AcceptanceStatus.Incomplete, "Two operator-confirmed display observations are required.");
        if (observations.Any(observation => observation.Hwnd == IntPtr.Zero || observation.ProcessId != expectedProcessId))
            return Result(AcceptanceStatus.Failed, "An observation did not come from the isolated pet window process.");
        if (observations[0].Hwnd != observations[1].Hwnd)
            return Result(AcceptanceStatus.Failed, "Both observations must use the same pet HWND.");

        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var target = availableMonitors.FirstOrDefault(monitor => monitor.Alias == observation.TargetAlias);
            if (target is null || target.Alias != availableMonitors[index].Alias ||
                observation.MonitorAlias != target.Alias || observation.WorkArea != target.WorkArea ||
                observation.MonitorBounds != target.Bounds)
                return Result(AcceptanceStatus.Failed,
                    $"The pet was not observed on the requested display ({observation.TargetAlias}).");
            if (observation.Dpi == 0)
                return Result(AcceptanceStatus.Failed, $"{target.Alias} returned no valid HWND DPI measurement.");
        }

        if (observations[0].Dpi == observations[1].Dpi)
            return Result(AcceptanceStatus.Skipped,
                $"Mixed-DPI verification is not supported by this display pair: {observations[0].TargetAlias} and " +
                $"{observations[1].TargetAlias} both measured equal DPI values ({observations[0].Dpi} DPI) on the same pet HWND.");

        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var target = availableMonitors.FirstOrDefault(monitor => monitor.Alias == observation.TargetAlias);
            if (target is null || target.Alias != availableMonitors[index].Alias ||
                observation.MonitorAlias != target.Alias || observation.WorkArea != target.WorkArea ||
                observation.MonitorBounds != target.Bounds)
                return Result(AcceptanceStatus.Failed,
                    $"The pet was not observed on the requested display ({observation.TargetAlias}).");
            if (observation.Dpi == 0 || !observation.Bounds.IsValid)
                return Result(AcceptanceStatus.Failed, $"{target.Alias} has invalid physical geometry or DPI.");

            int expectedWidth;
            int expectedHeight;
            try
            {
                expectedWidth = DpiGeometryPolicy.ToPhysicalExtent(PetWidthDip, observation.Dpi);
                expectedHeight = DpiGeometryPolicy.ToPhysicalExtent(PetHeightDip, observation.Dpi);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Result(AcceptanceStatus.Failed, $"{target.Alias} has an invalid measured DPI value.");
            }

            if (observation.Bounds.Width != expectedWidth || observation.Bounds.Height != expectedHeight)
                return Result(AcceptanceStatus.Failed,
                    $"{target.Alias} physical bounds do not match the pet's DPI-scaled WPF size.");

            var area = new MonitorWorkArea(target.Alias, target.WorkArea.Left, target.WorkArea.Top,
                target.WorkArea.Width, target.WorkArea.Height, target.IsPrimary);
            var clamped = PetPlacementPolicy.Clamp(
                new PetPosition(observation.Bounds.Left, observation.Bounds.Top),
                expectedWidth,
                expectedHeight,
                area,
                PetPlacementPolicy.DefaultMinimumVisiblePixels);
            var expectedLeft = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Left);
            var expectedTop = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Top);
            var visibleWidth = Math.Max(0, Math.Min(observation.Bounds.Right, target.WorkArea.Right) -
                Math.Max(observation.Bounds.Left, target.WorkArea.Left));
            var visibleHeight = Math.Max(0, Math.Min(observation.Bounds.Bottom, target.WorkArea.Bottom) -
                Math.Max(observation.Bounds.Top, target.WorkArea.Top));
            if (observation.Bounds.Left != expectedLeft || observation.Bounds.Top != expectedTop ||
                visibleWidth < PetPlacementPolicy.DefaultMinimumVisiblePixels ||
                visibleHeight < PetPlacementPolicy.DefaultMinimumVisiblePixels)
                return Result(AcceptanceStatus.Failed,
                    $"{target.Alias} physical bounds violate placement policy or the 20-pixel visible margin.");
        }

        var details = observations.Select(observation =>
            $"{observation.TargetAlias}: {observation.Dpi} DPI, bounds " +
            $"[{observation.Bounds.Left},{observation.Bounds.Top},{observation.Bounds.Right},{observation.Bounds.Bottom}], " +
            $"work area [{observation.WorkArea.Left},{observation.WorkArea.Top},{observation.WorkArea.Right},{observation.WorkArea.Bottom}]");
        return Result(AcceptanceStatus.Passed,
            "The same live pet HWND was observed on both requested displays with distinct DPI values and policy-compliant physical bounds. " +
            string.Join("; ", details) + ".");
    }

    internal static AcceptanceResult ValidateRemovalRecovery(
        DisplayRemovalRequest request,
        IReadOnlyList<DisplayAcceptanceMonitor> currentMonitors,
        DisplayAcceptanceObservation observation,
        uint expectedProcessId,
        bool appIsRunning)
    {
        if (!appIsRunning)
            return Result(AcceptanceStatus.Failed, "The isolated Desktop Puppy process was not running at the end of topology recovery.");
        if (request.Hwnd == IntPtr.Zero || observation.Hwnd != request.Hwnd ||
            observation.ProcessId != expectedProcessId || request.ProcessId != expectedProcessId)
            return Result(AcceptanceStatus.Failed, "Topology recovery was not measured on the original isolated pet HWND and process.");
        if (currentMonitors.Any(item => string.Equals(item.DeviceId, request.TargetDeviceId, StringComparison.Ordinal)))
            return Result(AcceptanceStatus.Failed, $"{request.TargetAlias} was still present in the refreshed monitor enumeration.");

        var primary = currentMonitors.FirstOrDefault(item =>
            item.IsPrimary && string.Equals(item.DeviceId, request.PrimaryDeviceId, StringComparison.Ordinal));
        if (primary is null || primary.Alias != request.PrimaryAlias)
            return Result(AcceptanceStatus.Failed, "The captured primary display did not remain connected as the primary display.");
        if (observation.MonitorAlias != primary.Alias || observation.WorkArea != primary.WorkArea ||
            observation.MonitorBounds != primary.Bounds || primary.WorkArea != request.PrimaryWorkArea)
            return Result(AcceptanceStatus.Failed, "The actual pet HWND was not measured on the captured primary work area.");
        if (observation.Dpi == 0 || !observation.Bounds.IsValid || !request.PreRemovalBounds.IsValid)
            return Result(AcceptanceStatus.Failed, "The captured request or actual fallback returned invalid physical geometry or HWND DPI.");

        int width;
        int height;
        int expectedLeft;
        int expectedTop;
        try
        {
            width = DpiGeometryPolicy.ToPhysicalExtent(PetWidthDip, observation.Dpi);
            height = DpiGeometryPolicy.ToPhysicalExtent(PetHeightDip, observation.Dpi);
            var area = new MonitorWorkArea(primary.Alias,
                request.PrimaryWorkArea.Left, request.PrimaryWorkArea.Top,
                request.PrimaryWorkArea.Width, request.PrimaryWorkArea.Height, IsPrimary: true);
            var clamped = PetPlacementPolicy.Clamp(
                new PetPosition(request.PreRemovalBounds.Left, request.PreRemovalBounds.Top),
                width, height, area, PetPlacementPolicy.DefaultMinimumVisiblePixels);
            expectedLeft = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Left);
            expectedTop = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Top);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result(AcceptanceStatus.Failed, "The live HWND DPI or captured primary work area is invalid for fallback calculation.");
        }
        catch (OverflowException)
        {
            return Result(AcceptanceStatus.Failed, "The expected primary fallback rectangle exceeds physical coordinate limits.");
        }

        PhysicalRect expected;
        try
        {
            expected = new PhysicalRect(expectedLeft, expectedTop,
                checked(expectedLeft + width), checked(expectedTop + height));
        }
        catch (OverflowException)
        {
            return Result(AcceptanceStatus.Failed, "The expected primary fallback rectangle exceeds physical coordinate limits.");
        }
        static bool WithinOnePixel(int actual, int expected) => Math.Abs((long)actual - expected) <= 1;
        if (!WithinOnePixel(observation.Bounds.Left, expected.Left) ||
            !WithinOnePixel(observation.Bounds.Top, expected.Top) ||
            !WithinOnePixel(observation.Bounds.Right, expected.Right) ||
            !WithinOnePixel(observation.Bounds.Bottom, expected.Bottom))
            return Result(AcceptanceStatus.Failed,
                $"The actual HWND bounds on {primary.Alias} differ by more than 1 physical pixel from the fallback calculated from the captured pre-removal request.");

        var visibleWidth = Math.Max(0L,
            (long)Math.Min(observation.Bounds.Right, request.PrimaryWorkArea.Right) -
            Math.Max(observation.Bounds.Left, request.PrimaryWorkArea.Left));
        var visibleHeight = Math.Max(0L,
            (long)Math.Min(observation.Bounds.Bottom, request.PrimaryWorkArea.Bottom) -
            Math.Max(observation.Bounds.Top, request.PrimaryWorkArea.Top));
        if (visibleWidth < PetPlacementPolicy.DefaultMinimumVisiblePixels ||
            visibleHeight < PetPlacementPolicy.DefaultMinimumVisiblePixels)
            return Result(AcceptanceStatus.Failed,
                $"The actual HWND on {primary.Alias} has fewer than 20 visible physical pixels on an edge.");

        return Result(AcceptanceStatus.Passed,
            $"{request.TargetAlias} was removed after a real display-change event; the live pet HWND remained running on {primary.Alias} at {observation.Dpi} DPI. " +
            $"Captured pre-removal bounds [{request.PreRemovalBounds.Left},{request.PreRemovalBounds.Top},{request.PreRemovalBounds.Right},{request.PreRemovalBounds.Bottom}], " +
            $"expected primary fallback [{expected.Left},{expected.Top},{expected.Right},{expected.Bottom}], actual bounds " +
            $"[{observation.Bounds.Left},{observation.Bounds.Top},{observation.Bounds.Right},{observation.Bounds.Bottom}], " +
            $"primary work area [{request.PrimaryWorkArea.Left},{request.PrimaryWorkArea.Top},{request.PrimaryWorkArea.Right},{request.PrimaryWorkArea.Bottom}], " +
            "all physical edges within 1 pixel and at least 20 physical pixels visible.");
    }

    private static AcceptanceResult Result(AcceptanceStatus status, string summary) =>
        new(status, summary, Array.Empty<ObservedSystemEvent>(), Array.Empty<string>());
}

internal sealed record DisplayAcceptanceMonitor(
    string Alias,
    string DeviceId,
    PhysicalRect Bounds,
    PhysicalRect WorkArea,
    bool IsPrimary);

internal sealed class DisplayRemovalPreconditionException(string message) : Exception(message);

internal sealed record DisplayRemovalRequest(
    string TargetAlias,
    string TargetDeviceId,
    string PrimaryAlias,
    string PrimaryDeviceId,
    PhysicalRect PreRemovalBounds,
    PhysicalRect PrimaryWorkArea,
    IntPtr Hwnd,
    uint ProcessId,
    int DisplayChangeGeneration);

internal sealed record DisplayAcceptanceObservation(
    string TargetAlias,
    IntPtr Hwnd,
    uint ProcessId,
    uint Dpi,
    PhysicalRect Bounds,
    string MonitorAlias,
    PhysicalRect MonitorBounds,
    PhysicalRect WorkArea);

internal sealed record NativeDisplaySnapshot(
    string DeviceId,
    PhysicalRect Bounds,
    PhysicalRect WorkArea,
    bool IsPrimary);

internal interface IDisplayAcceptanceNativeApi
{
    event EventHandler? DisplaySettingsChanged;
    IReadOnlyList<NativeDisplaySnapshot> EnumerateMonitors();
    IntPtr FindPetWindowHandle(uint processId);
    bool IsPetWindow(IntPtr hwnd);
    uint GetWindowProcessId(IntPtr hwnd);
    PhysicalRect GetWindowBounds(IntPtr hwnd);
    NativeDisplaySnapshot GetMonitorForWindow(IntPtr hwnd);
    uint GetDpiForWindow(IntPtr hwnd);
}

internal sealed class ReadOnlyDisplayAcceptanceNativeApi : IDisplayAcceptanceNativeApi, IDisposable
{
    private readonly Win32MonitorDisplayProvider displays;
    private readonly Win32WindowPositioner windows;

    internal ReadOnlyDisplayAcceptanceNativeApi()
    {
        displays = new Win32MonitorDisplayProvider();
        windows = new Win32WindowPositioner(new Win32WindowApi(), displays);
    }

    public event EventHandler? DisplaySettingsChanged
    {
        add => displays.DisplaySettingsChanged += value;
        remove => displays.DisplaySettingsChanged -= value;
    }

    public IReadOnlyList<NativeDisplaySnapshot> EnumerateMonitors() => displays.GetMonitors()
        .Select(ToSnapshot)
        .ToArray();

    public IntPtr FindPetWindowHandle(uint expectedProcessId)
    {
        try
        {
            var processIdCondition = new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                checked((int)expectedProcessId));
            var candidates = AutomationElement.RootElement.FindAll(TreeScope.Children, processIdCondition);
            foreach (AutomationElement candidate in candidates)
            {
                var current = candidate.Current;
                if (current.ControlType == ControlType.Window &&
                    string.Equals(current.AutomationId, "PetWindow", StringComparison.Ordinal) &&
                    current.NativeWindowHandle != 0)
                    return new IntPtr(current.NativeWindowHandle);
            }
        }
        catch (COMException)
        {
            // The caller retries UI Automation discovery until its bounded startup deadline.
        }
        catch (ElementNotAvailableException)
        {
            // The caller retries if WPF replaces an automation peer during startup.
        }

        return IntPtr.Zero;
    }

    public bool IsPetWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            return element is not null &&
                element.Current.ControlType == ControlType.Window &&
                element.Current.NativeWindowHandle == hwnd.ToInt32() &&
                string.Equals(element.Current.AutomationId, "PetWindow", StringComparison.Ordinal);
        }
        catch (COMException) { return false; }
        catch (ElementNotAvailableException) { return false; }
    }

    public uint GetWindowProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            return element is null ? 0 : checked((uint)element.Current.ProcessId);
        }
        catch (COMException) { return 0; }
        catch (ElementNotAvailableException) { return 0; }
    }

    public PhysicalRect GetWindowBounds(IntPtr hwnd) => windows.GetBounds(hwnd);

    public NativeDisplaySnapshot GetMonitorForWindow(IntPtr hwnd) => ToSnapshot(windows.GetMonitorForWindow(hwnd));

    public uint GetDpiForWindow(IntPtr hwnd) => windows.GetDpi(hwnd);

    public void Dispose() => displays.Dispose();

    private static NativeDisplaySnapshot ToSnapshot(MonitorDisplayInfo monitor) => new(
        monitor.DeviceId, monitor.Bounds, monitor.WorkArea, monitor.IsPrimary);
}
