using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPuppy.Native;

public sealed class WinEventForegroundWatcher : IForegroundWindowWatcher
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _threadReady = new(false);
    private readonly ManualResetEventSlim _threadStopped = new(false);
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private WinEventDelegate? _callback;
    private Action<ForegroundWindowSnapshot>? _onForegroundChanged;
    private bool _disposed;

    public void Start(Action<ForegroundWindowSnapshot> onForegroundChanged)
    {
        ArgumentNullException.ThrowIfNull(onForegroundChanged);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                throw new InvalidOperationException("The foreground watcher is already started.");
            }

            _onForegroundChanged = onForegroundChanged;
            _threadReady.Reset();
            _threadStopped.Reset();
            _thread = new Thread(InstallHookAndPump)
            {
                IsBackground = true,
                Name = "DesktopPuppy.ForegroundWindowWatcher"
            };
            _thread.Start();
        }

        if (!_threadReady.Wait(TimeSpan.FromSeconds(2)))
        {
            Stop();
            throw new TimeoutException("The foreground window hook thread did not start in time.");
        }

        if (_hook == 0)
        {
            Stop();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWinEventHook failed.");
        }
    }

    public void RefreshForegroundWindow()
    {
        Action<ForegroundWindowSnapshot>? callback;
        lock (_gate)
        {
            if (_disposed || _thread is null)
            {
                return;
            }

            callback = _onForegroundChanged;
        }

        var windowHandle = GetForegroundWindow();
        if (windowHandle == 0)
        {
            return;
        }

        var snapshot = ReadSnapshot(windowHandle);
        if (snapshot is null)
        {
            return;
        }

        try
        {
            callback?.Invoke(snapshot);
        }
        catch
        {
            // A consumer exception must not destabilize the foreground watcher.
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_gate)
        {
            thread = _thread;
            threadId = _threadId;
        }

        if (thread is null)
        {
            return;
        }

        if (threadId != 0)
        {
            _ = PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, nint.Zero);
        }

        if (Thread.CurrentThread != thread)
        {
            _ = _threadStopped.Wait(TimeSpan.FromSeconds(2));
            thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (_gate)
        {
            if (ReferenceEquals(_thread, thread))
            {
                _thread = null;
                _threadId = 0;
                _onForegroundChanged = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _threadReady.Dispose();
        _threadStopped.Dispose();
    }

    private void InstallHookAndPump()
    {
        _threadId = GetCurrentThreadId();
        _callback = HandleWinEvent;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);

        _ = PeekMessage(out _, nint.Zero, 0, 0, PmNoRemove);
        _threadReady.Set();

        if (_hook == 0)
        {
            _threadStopped.Set();
            return;
        }

        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }

        _ = UnhookWinEvent(_hook);
        _hook = 0;
        _callback = null;
        _threadStopped.Set();
    }

    private void HandleWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType != EventSystemForeground || windowHandle == 0)
        {
            return;
        }

        var snapshot = ReadSnapshot(windowHandle);
        if (snapshot is null)
        {
            return;
        }

        try
        {
            _onForegroundChanged?.Invoke(snapshot);
        }
        catch
        {
            // A consumer exception must not tear down the native hook thread.
        }
    }

    private static ForegroundWindowSnapshot? ReadSnapshot(nint windowHandle)
    {
        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            processName = "Unknown";
        }
        catch (InvalidOperationException)
        {
            processName = "Unknown";
        }

        var titleLength = GetWindowTextLength(windowHandle);
        var title = titleLength == 0
            ? string.Empty
            : ReadWindowTitle(windowHandle, titleLength);

        return new ForegroundWindowSnapshot(windowHandle, processId, processName, title);
    }

    private static string ReadWindowTitle(nint windowHandle, int titleLength)
    {
        var buffer = new StringBuilder(titleLength + 1);
        _ = GetWindowText(windowHandle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Handle;
        public uint MessageId;
        public UIntPtr WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint moduleHandle,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, nint windowHandle, uint minimumFilter, uint maximumFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out Message message,
        nint windowHandle,
        uint minimumFilter,
        uint maximumFilter,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumTextLength);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
