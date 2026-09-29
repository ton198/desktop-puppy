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
    private IReadOnlyList<DisplayAcceptanceMonitor>? monitors;

    internal DisplayAcceptanceScenario(IDisplayAcceptanceNativeApi native) =>
        this.native = native ?? throw new ArgumentNullException(nameof(native));

    public void Dispose()
    {
        if (native is IDisposable disposable) disposable.Dispose();
    }

    internal IReadOnlyList<DisplayAcceptanceMonitor> GetMonitors()
    {
        if (monitors is not null) return monitors;
        var enumerated = native.EnumerateMonitors();
        monitors = enumerated.Select((monitor, index) => new DisplayAcceptanceMonitor(
            $"Display {index + 1}", monitor.DeviceId, monitor.Bounds, monitor.WorkArea, monitor.IsPrimary)).ToArray();
        return monitors;
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

    private static AcceptanceResult Result(AcceptanceStatus status, string summary) =>
        new(status, summary, Array.Empty<ObservedSystemEvent>(), Array.Empty<string>());
}

internal sealed record DisplayAcceptanceMonitor(
    string Alias,
    string DeviceId,
    PhysicalRect Bounds,
    PhysicalRect WorkArea,
    bool IsPrimary);

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
