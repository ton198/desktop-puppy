namespace DesktopPuppy.Native;

public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    private readonly object _gate = new();
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _stopEvent;
    private ManualResetEventSlim? _ownerReady;
    private Thread? _ownerThread;
    private Action? _activateExisting;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceGuard()
        : this("Local\\DesktopPuppy.SingleInstance", "Local\\DesktopPuppy.Activate")
    {
    }
    public SingleInstanceGuard(string mutexName, string activationEventName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));
        }

        if (string.IsNullOrWhiteSpace(activationEventName))
        {
            throw new ArgumentException("An activation event name is required.", nameof(activationEventName));
        }

        _mutexName = mutexName;
        _activationEventName = activationEventName;
    }

    public bool TryAcquire(Action activateExisting)
    {
        ArgumentNullException.ThrowIfNull(activateExisting);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ownsMutex)
            {
                return true;
            }
        }

        var mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            SignalExistingInstance();
            return false;
        }

        EventWaitHandle? activationEvent = null;
        EventWaitHandle? stopEvent = null;
        ManualResetEventSlim? ownerReady = null;
        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                _activationEventName);
            stopEvent = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
            ownerReady = new ManualResetEventSlim(false);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _mutex = mutex;
                _activationEvent = activationEvent;
                _stopEvent = stopEvent;
                _ownerReady = ownerReady;
                _activateExisting = activateExisting;
                _ownerThread = new Thread(OwnMutexAndListen)
                {
                    IsBackground = true,
                    Name = "DesktopPuppy.SingleInstanceActivation"
                };
                _ownerThread.Start();
            }

            if (!ownerReady.Wait(TimeSpan.FromSeconds(2)))
            {
                Dispose();
                throw new TimeoutException("The single-instance guard thread did not acquire the mutex in time.");
            }

            lock (_gate)
            {
                _ownsMutex = true;
            }

            return true;
        }
        catch
        {
            activationEvent?.Dispose();
            stopEvent?.Dispose();
            ownerReady?.Dispose();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Thread? ownerThread;
        EventWaitHandle? stopEvent;
        EventWaitHandle? activationEvent;
        ManualResetEventSlim? ownerReady;
        Mutex? mutex;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ownerThread = _ownerThread;
            stopEvent = _stopEvent;
            activationEvent = _activationEvent;
            ownerReady = _ownerReady;
            mutex = _mutex;
            _ownerThread = null;
            _stopEvent = null;
            _activationEvent = null;
            _ownerReady = null;
            _mutex = null;
            _activateExisting = null;
            _ownsMutex = false;
        }

        stopEvent?.Set();
        if (ownerThread is not null && Thread.CurrentThread != ownerThread)
        {
            ownerThread.Join(TimeSpan.FromSeconds(2));
        }

        stopEvent?.Dispose();
        activationEvent?.Dispose();
        ownerReady?.Dispose();
        mutex?.Dispose();
    }

    private void OwnMutexAndListen()
    {
        Mutex? mutex;
        EventWaitHandle? activationEvent;
        EventWaitHandle? stopEvent;
        ManualResetEventSlim? ownerReady;

        lock (_gate)
        {
            mutex = _mutex;
            activationEvent = _activationEvent;
            stopEvent = _stopEvent;
            ownerReady = _ownerReady;
        }

        if (mutex is null || activationEvent is null || stopEvent is null || ownerReady is null)
        {
            return;
        }

        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            ownerReady.Set();
            if (!acquired)
            {
                return;
            }

            var handles = new WaitHandle[] { activationEvent, stopEvent };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try
                {
                    _activateExisting?.Invoke();
                }
                catch
                {
                    // The guard must keep listening even when activation handling fails.
                }
            }
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(_activationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance may be exiting; there is no existing target to activate.
        }
    }
}
