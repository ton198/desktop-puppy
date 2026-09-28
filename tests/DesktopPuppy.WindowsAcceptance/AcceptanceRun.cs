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
    private readonly SemaphoreSlim disposalGate = new(1, 1);
    private bool disposed;

    private AcceptanceRun(AcceptanceOwnedDirectory owner, Process app, Process fixtureA, Process fixtureB,
        SystemEventsSessionPowerEventSource eventSource, AcceptanceEventRecorder observer,
        string fixtureAId, string fixtureBId)
    {
        this.owner = owner;
        this.app = app;
        this.fixtureA = fixtureA;
        this.fixtureB = fixtureB;
        this.eventSource = eventSource;
        this.observer = observer;
        FixtureIds = new FixtureProcessIds((uint)fixtureA.Id, (uint)fixtureB.Id);
        FixtureAutomationIds = (fixtureAId, fixtureBId);
    }

    public FixtureProcessIds FixtureIds { get; }
    public (string A, string B) FixtureAutomationIds { get; }
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

            var run = new AcceptanceRun(owner, app, fixtureA, fixtureB, source, observer, fixtureAId, fixtureBId);
            source.Start((kind, at) =>
            {
                observer.Record(kind, at);
                run.EventsChanged?.Invoke(observer.Snapshot());
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

    public Task<AcceptanceResult> RunLockUnlockAsync(CancellationToken cancellationToken) => NotImplementedAsync(cancellationToken);
    public Task<AcceptanceResult> RunSuspendResumeAsync(CancellationToken cancellationToken) => NotImplementedAsync(cancellationToken);

    private Task<AcceptanceResult> NotImplementedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        // Tasks 3/4 add manual operator steps and persisted boundary checks. Events alone cannot pass.
        return Task.FromResult(observer.NotImplementedResult());
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
