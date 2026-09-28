using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DesktopPuppy.Core.Clock;
using DesktopPuppy.Usage;
using DesktopPuppy.WindowsAcceptance.Core;

namespace DesktopPuppy.WindowsAcceptance;

internal sealed class AcceptanceSkippedException(string reason) : Exception(reason);

public sealed class AcceptanceRun : IAsyncDisposable
{
    private readonly AcceptanceOwnedDirectory owner;
    private readonly Process app;
    private readonly Process fixtureA;
    private readonly Process fixtureB;
    private readonly SystemEventsSessionPowerEventSource eventSource;
    private readonly AcceptanceEventRecorder observer;
    private readonly string acceptanceProcessName;
    private readonly SemaphoreSlim eventSignal = new(0, 1);
    private readonly TaskCompletionSource<DateTimeOffset> lockUnlockConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim disposalGate = new(1, 1);
    private bool disposed;

    private AcceptanceRun(AcceptanceOwnedDirectory owner, Process app, Process fixtureA, Process fixtureB,
        SystemEventsSessionPowerEventSource eventSource, AcceptanceEventRecorder observer,
        string fixtureAId, string fixtureBId, TimeSpan stepTimeout)
    {
        this.owner = owner;
        this.app = app;
        this.fixtureA = fixtureA;
        this.fixtureB = fixtureB;
        this.eventSource = eventSource;
        this.observer = observer;
        acceptanceProcessName = fixtureA.ProcessName;
        StepTimeout = stepTimeout;
        FixtureIds = new FixtureProcessIds((uint)fixtureA.Id, (uint)fixtureB.Id);
        FixtureAutomationIds = (fixtureAId, fixtureBId);
    }

    public FixtureProcessIds FixtureIds { get; }
    public (string A, string B) FixtureAutomationIds { get; }
    public TimeSpan StepTimeout { get; }
    public IReadOnlyList<ObservedSystemEvent> ObservedEvents => observer.Snapshot();
    public event Action<IReadOnlyList<ObservedSystemEvent>>? EventsChanged;

    public static Task<AcceptanceRun> StartAsync(string appExecutablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Repeat the preflight immediately before allocating files: the app's single-instance
        // guard otherwise activates an unrelated running product rather than this isolated one.
        var reason = WindowsAcceptancePrerequisite.SkipReason();
        if (reason is not null) throw new AcceptanceSkippedException(reason);
        if (!string.Equals(Path.GetFullPath(appExecutablePath),
                Path.GetFullPath(Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE")!), StringComparison.OrdinalIgnoreCase))
            throw new AcceptanceSkippedException("Product executable changed since prerequisite check.");

        var stepTimeout = AcceptanceStepTimeout.Read();
        AcceptanceOwnedDirectory? owner = null;
        Process? app = null, fixtureA = null, fixtureB = null;
        SystemEventsSessionPowerEventSource? source = null;
        try
        {
            owner = AcceptanceOwnedDirectory.Create();
            var databasePath = Path.GetFullPath(owner.DatabasePath);
            var directory = Path.GetDirectoryName(databasePath)!;
            if (!Directory.Exists(directory) ||
                !string.Equals(directory, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The owned database directory is unavailable.");
            using (var store = new UsageSqliteStore(databasePath)) store.Initialize();
            if (!File.Exists(databasePath) ||
                !string.Equals(Path.GetDirectoryName(databasePath), directory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The isolated database was not created in the owned directory.");
            cancellationToken.ThrowIfCancellationRequested();

            var observer = new AcceptanceEventRecorder();
            source = new SystemEventsSessionPowerEventSource(new SystemClock());
            app = StartChild(new ProcessStartInfo(appExecutablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(appExecutablePath), UseShellExecute = false
            }, databasePath);
            cancellationToken.ThrowIfCancellationRequested();
            if (app.HasExited) throw new InvalidOperationException("The isolated product exited during startup.");

            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Controller executable is unavailable.");
            var runId = Guid.NewGuid().ToString("N");
            var fixtureAId = $"DesktopPuppyAcceptance-{runId}-A";
            var fixtureBId = $"DesktopPuppyAcceptance-{runId}-B";
            fixtureA = StartFixture(executable, runId, "A");
            cancellationToken.ThrowIfCancellationRequested();
            fixtureB = StartFixture(executable, runId, "B");
            cancellationToken.ThrowIfCancellationRequested();
            if (fixtureA.Id == fixtureB.Id || fixtureA.HasExited || fixtureB.HasExited || app.HasExited)
                throw new InvalidOperationException("An acceptance child exited during startup.");

            var run = new AcceptanceRun(owner, app, fixtureA, fixtureB, source, observer,
                fixtureAId, fixtureBId, stepTimeout);
            source.Start((kind, at) =>
            {
                observer.Record(kind, at);
                run.EventsChanged?.Invoke(observer.Snapshot());
                try { run.eventSignal.Release(); }
                catch (SemaphoreFullException) { /* Coalesce excess notifications; the observer retains all events. */ }
            });
            return Task.FromResult(run);
        }
        catch
        {
            source?.Dispose();
            StopChildrenAsync(fixtureB, fixtureA, app).GetAwaiter().GetResult();
            owner?.DeleteOwned();
            throw;
        }
    }

    private static Process StartFixture(string executable, string runId, string fixtureId)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add("--fixture");
        info.ArgumentList.Add(runId);
        info.ArgumentList.Add(fixtureId);
        return StartChild(info);
    }

    private static Process StartChild(ProcessStartInfo info, string? databasePath = null)
    {
        if (databasePath is not null) info.Environment["DESKTOP_PUPPY_USAGE_DB_PATH"] = databasePath;
        return Process.Start(info) ?? throw new InvalidOperationException("An acceptance child could not start.");
    }

    // Purpose: prove that real lock/unlock notifications split only the isolated fixture sessions.
    // Coverage: ordered observer events, fixture A pre/post rows, fixture B close, and operator confirmation.
    // Target effect: only closed fixture rows meeting the five-second locked-time boundaries can pass.
    public async Task<AcceptanceResult> RunLockUnlockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        using var deadline = new CancellationTokenSource(StepTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var pendingStep = "the Windows Locked notification";
        try
        {
            while (!HasOrderedLockNotifications(observer.Snapshot()))
            {
                var events = observer.Snapshot();
                if (HasInvalidLockNotificationOrder(events))
                    return new AcceptanceResult(AcceptanceStatus.Failed,
                        "Real lock/unlock notifications were repeated or out of order.", events, Array.Empty<string>());
                pendingStep = events.Any(e => e.Kind == ObservedSystemEventKind.Locked)
                    ? "the Windows Unlocked notification"
                    : "the Windows Locked notification";
                await eventSignal.WaitAsync(linked.Token);
            }

            pendingStep = "fixture A-to-B switching and controller confirmation";
            var completedAt = await lockUnlockConfirmation.Task.WaitAsync(linked.Token);
            using var store = new UsageSqliteStore(owner.DatabasePath);
            var sessions = store.ReadSessions()
                .Where(session => (session.ProcessId == FixtureIds.FixtureAProcessId ||
                    session.ProcessId == FixtureIds.FixtureBProcessId) &&
                    string.Equals(session.ProcessName, acceptanceProcessName, StringComparison.OrdinalIgnoreCase) &&
                    session.EndedAt is not null)
                .Select(session => new ObservedUsageSession(session.ProcessId, session.StartedAt, session.EndedAt!.Value))
                .ToArray();
            return new SessionBoundaryValidator().ValidateLockUnlock(
                observer.Snapshot(), sessions, FixtureIds, completedAt, operatorConfirmed: true);
        }
        catch (OperationCanceledException)
        {
            var reason = cancellationToken.IsCancellationRequested ? "Operator cancelled" : "Timed out";
            return new AcceptanceResult(AcceptanceStatus.Incomplete,
                $"{reason} while waiting for {pendingStep}; no acceptance verdict was produced.",
                observer.Snapshot(), [$"Pending step: {pendingStep}."]);
        }
    }

    public void ConfirmLockUnlockCompletion(DateTimeOffset completedAt)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lockUnlockConfirmation.TrySetResult(completedAt);
    }

    // Purpose: preserve the separate suspend/resume scenario entry point for Task 4.
    // Coverage: this Task 3 build does not collect suspend/resume operator input.
    // Target effect: no incomplete suspend/resume scenario may pass.
    public Task<AcceptanceResult> RunSuspendResumeAsync(CancellationToken cancellationToken) => NotImplementedAsync(cancellationToken);

    private Task<AcceptanceResult> NotImplementedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        // Task 4 adds the suspend/resume operator flow; events alone cannot pass.
        return Task.FromResult(observer.NotImplementedResult());
    }

    private static bool HasOrderedLockNotifications(IReadOnlyList<ObservedSystemEvent> events)
    {
        var relevant = events.Where(e => e.Kind is ObservedSystemEventKind.Locked or ObservedSystemEventKind.Unlocked).ToArray();
        return relevant.Length == 2 && relevant[0].Kind == ObservedSystemEventKind.Locked &&
            relevant[1].Kind == ObservedSystemEventKind.Unlocked && relevant[0].At < relevant[1].At;
    }

    private static bool HasInvalidLockNotificationOrder(IReadOnlyList<ObservedSystemEvent> events)
    {
        var relevant = events.Where(e => e.Kind is ObservedSystemEventKind.Locked or ObservedSystemEventKind.Unlocked).ToArray();
        return relevant.Length > 0 && relevant[0].Kind != ObservedSystemEventKind.Locked ||
            relevant.Length >= 2 && !HasOrderedLockNotifications(events);
    }

    public async ValueTask DisposeAsync()
    {
        await disposalGate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            eventSource.Dispose();
            EventsChanged = null;
            await StopChildrenAsync(fixtureB, fixtureA, app);
            owner.DeleteOwned();
        }
        finally { disposalGate.Release(); }
    }

    private static async Task StopChildrenAsync(params Process?[] children)
    {
        Exception? failure = null;
        foreach (var child in children)
        {
            if (child is null) continue;
            try
            {
                if (!child.HasExited)
                {
                    child.CloseMainWindow();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await child.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException)
                    {
                        if (!child.HasExited) child.Kill();
                        await child.WaitForExitAsync();
                    }
                }
            }
            catch (InvalidOperationException) { /* Already exited. */ }
            catch (Win32Exception ex) { failure ??= new IOException("Could not stop an owned acceptance child.", ex); }
            finally { child.Dispose(); }
        }
        if (failure is not null) throw failure;
    }
}

internal static class AcceptanceStepTimeout
{
    internal const int DefaultMinutes = 30;
    internal const int MinimumMinutes = 1;
    internal const int MaximumMinutes = 120;

    internal static bool TryRead(string? value, out TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            timeout = TimeSpan.FromMinutes(DefaultMinutes);
            return true;
        }
        if (int.TryParse(value, out var minutes) && minutes is >= MinimumMinutes and <= MaximumMinutes)
        {
            timeout = TimeSpan.FromMinutes(minutes);
            return true;
        }
        timeout = default;
        return false;
    }

    internal static TimeSpan Read()
    {
        var value = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES");
        if (TryRead(value, out var timeout)) return timeout;
        throw new InvalidOperationException("DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES must be a whole number from 1 to 120.");
    }
}

public sealed class AcceptanceEventRecorder
{
    private readonly object gate = new();
    private readonly List<ObservedSystemEvent> events = [];

    public void Record(SessionPowerEvent kind, DateTimeOffset at)
    {
        ObservedSystemEventKind? mapped = kind switch
        {
            SessionPowerEvent.Locked => ObservedSystemEventKind.Locked,
            SessionPowerEvent.Unlocked => ObservedSystemEventKind.Unlocked,
            SessionPowerEvent.Suspend => ObservedSystemEventKind.Suspend,
            SessionPowerEvent.Resume => ObservedSystemEventKind.Resume,
            _ => null
        };
        if (mapped is null) return;
        lock (gate) events.Add(new ObservedSystemEvent(mapped.Value, at));
    }

    public IReadOnlyList<ObservedSystemEvent> Snapshot()
    {
        lock (gate) return events.ToArray();
    }

    public AcceptanceResult NotImplementedResult() => new(AcceptanceStatus.Incomplete,
        "Manual steps and persisted session boundary validation are not yet implemented; no acceptance verdict was produced.",
        Snapshot(), Array.Empty<string>());
}
