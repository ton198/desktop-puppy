using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;

public sealed class ContextForegroundWatcherTests
{
    [Fact] public void Context_is_observed_before_statistics_start_and_after_statistics_stop()
    {
        var inner = new FakeWatcher();
        using var watcher = new ContextForegroundWatcher(inner, "DesktopPuppy");
        inner.Emit("editor");
        Assert.Equal("editor", watcher.LastExternal?.ProcessName);
        var calls = 0;
        watcher.Start(_ => calls++);
        inner.Emit("browser");
        watcher.Stop();
        inner.Emit("game");
        Assert.Equal(1, calls);
        Assert.Equal("game", watcher.LastExternal?.ProcessName);
        Assert.Equal(1, inner.StartCount);
        Assert.Equal(0, inner.StopCount);
    }

    [Fact] public void Restart_replaces_collector_callback_without_installing_another_hook()
    {
        var inner = new FakeWatcher();
        using var watcher = new ContextForegroundWatcher(inner, "DesktopPuppy");
        var oldCalls = 0; var newCalls = 0;
        watcher.Start(_ => oldCalls++);
        watcher.Start(_ => newCalls++);
        inner.Emit("editor");
        watcher.Stop();
        watcher.Start(_ => newCalls++);
        inner.Emit("DESKTOPPUPPY");
        Assert.Equal(0, oldCalls);
        Assert.Equal(2, newCalls);
        Assert.Equal("editor", watcher.LastExternal?.ProcessName);
        Assert.Equal(1, inner.StartCount);
    }

    [Fact] public void Dispose_stops_shared_hook_once_and_ignores_late_native_callbacks()
    {
        var inner = new FakeWatcher();
        var watcher = new ContextForegroundWatcher(inner, "DesktopPuppy");
        var calls = 0;
        watcher.Start(_ => calls++);
        inner.Emit("editor");
        watcher.Dispose(); watcher.Dispose();
        inner.Emit("late");
        Assert.Equal(1, calls);
        Assert.Equal("editor", watcher.LastExternal?.ProcessName);
        Assert.Equal(1, inner.StopCount);
        Assert.Equal(1, inner.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => watcher.Start(_ => { }));
    }

    private sealed class FakeWatcher : IForegroundWindowWatcher
    {
        public int StartCount, StopCount, DisposeCount;
        private Action<ForegroundWindowSnapshot>? _callback;
        public void Start(Action<ForegroundWindowSnapshot> callback) { StartCount++; _callback = callback; }
        public void Stop() => StopCount++;
        public void Dispose() => DisposeCount++;
        public void RefreshForegroundWindow() { }
        public void Emit(string process) => _callback?.Invoke(new(1, 1, process, "test"));
    }
}
