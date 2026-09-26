using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DesktopPuppy.UiTests;

public sealed class DesktopPuppyUiHarness : IDisposable
{
    private const string WindowTitle = "Desktop Puppy";
    private const int ShowNormal = 1;
    private readonly Process _process;
    private IntPtr _windowHandle;
    private bool _disposed;

    private DesktopPuppyUiHarness(Process process)
    {
        _process = process;
    }

    public static DesktopPuppyUiHarness Start(string executablePath, string? usageDatabasePath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false
        };
        if (usageDatabasePath is not null)
        {
            startInfo.Environment["DESKTOP_PUPPY_USAGE_DB_PATH"] = usageDatabasePath;
        }

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Could not start '{executablePath}'.");
        }

        return new DesktopPuppyUiHarness(process);
    }

    public AutomationElement WaitForPetWindow(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            _windowHandle = FindVisibleWindow(_process.Id);
            if (_windowHandle != IntPtr.Zero)
            {
                try
                {
                    var element = AutomationElement.FromHandle(_windowHandle);
                    if (element is not null)
                    {
                        return element;
                    }
                }
                catch (COMException)
                {
                    // UIA can race WPF while the HWND is entering the automation tree.
                }
                catch (ElementNotAvailableException)
                {
                    // Retry until the WPF automation peer is ready.
                }
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"No visible '{WindowTitle}' window was found for process {_process.Id}.");
    }

    public AutomationElement WaitForTopLevelWindow(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = FindVisibleTopLevelWindow(_process.Id, automationId);
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException(
            $"Top-level UI Automation window '{automationId}' was not found for process {_process.Id}.");
    }
    public AutomationElement FindByAutomationId(AutomationElement root, string automationId)
    {
        var condition = new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            automationId);
        return root.FindFirst(TreeScope.Descendants, condition)
            ?? throw new InvalidOperationException($"UI Automation element '{automationId}' was not found.");
    }

    public AutomationElement WaitForAutomationId(
        AutomationElement root,
        string automationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var condition = new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    automationId);
                var element = root.FindFirst(TreeScope.Descendants, condition);
                if (element is not null && !element.Current.IsOffscreen)
                {
                    return element;
                }
            }
            catch (COMException)
            {
                // Retry while UIA refreshes the WPF tree.
            }
            catch (ElementNotAvailableException)
            {
                // Retry while WPF replaces an element.
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"UI Automation element '{automationId}' did not become visible.");
    }

    public void WaitForAutomationIdToDisappear(
        AutomationElement root,
        string automationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var condition = new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    automationId);
                var element = root.FindFirst(TreeScope.Descendants, condition);
                if (element is null || element.Current.IsOffscreen)
                {
                    return;
                }
            }
            catch (COMException)
            {
                return;
            }
            catch (ElementNotAvailableException)
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"UI Automation element '{automationId}' remained visible.");
    }

    public IReadOnlyList<string> WaitForListItemNames(
        AutomationElement root,
        string listAutomationId,
        int expectedCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        IReadOnlyList<string> lastNames = [];
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var list = FindByAutomationId(root, listAutomationId);
                var items = list.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                lastNames = items.Cast<AutomationElement>().Select(ReadListItemText).ToArray();
                if (lastNames.Count == expectedCount)
                {
                    return lastNames;
                }
            }
            catch (InvalidOperationException)
            {
                // Retry until the list is populated.
            }
            catch (ElementNotAvailableException)
            {
                // Retry while WPF refreshes the list.
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException(
            $"List '{listAutomationId}' did not contain {expectedCount} items. Last items: {string.Join(" | ", lastNames)}");
    }

    public void SelectComboBoxItem(AutomationElement root, string comboAutomationId, string itemName)
    {
        var combo = FindByAutomationId(root, comboAutomationId);
        var expandPattern = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expandPattern.Expand();
        var containerPattern = (ItemContainerPattern)combo.GetCurrentPattern(ItemContainerPattern.Pattern);
        var item = containerPattern.FindItemByProperty(
            startAfter: null,
            AutomationElement.NameProperty,
            itemName);
        if (item is null && DateOnly.TryParseExact(itemName, "yyyy-MM-dd", out var date))
        {
            item = containerPattern.FindItemByProperty(
                startAfter: null,
                AutomationElement.NameProperty,
                date.ToString());
        }

        if (item is null)
        {
            throw new InvalidOperationException($"Combo box item '{itemName}' was not found.");
        }
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        expandPattern.Collapse();
    }

    public void SelectListItemContaining(AutomationElement root, string listAutomationId, string text)
    {
        var list = FindByAutomationId(root, listAutomationId);
        var items = list.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var item = items.Cast<AutomationElement>().FirstOrDefault(
            candidate => ReadListItemText(candidate).Contains(text, StringComparison.Ordinal));
        if (item is null)
        {
            throw new InvalidOperationException($"List item containing '{text}' was not found.");
        }

        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    public void CloseWindow(AutomationElement window)
    {
        ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
    }

    public void WaitForTopLevelWindowToDisappear(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindVisibleTopLevelWindow(_process.Id, automationId) is null)
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Top-level window '{automationId}' remained visible.");
    }

    public int CountTopLevelWindows(string automationId) =>
        CountVisibleTopLevelWindows(_process.Id, automationId);

    public bool IsProcessRunning => !_process.HasExited;

    public void Invoke(AutomationElement element)
    {
        var pattern = (InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern);
        pattern.Invoke();
    }

    public void RightClickCenter()
    {
        BringToFront();
        var center = GetPetInteractionPoint();
        SetCursorPos(center.X, center.Y);
        SendMouseButton(MouseButton.Right, pressed: true);
        SendMouseButton(MouseButton.Right, pressed: false);
    }

    public void DoubleClickCenter()
    {
        BringToFront();
        var center = GetPetInteractionPoint();
        SetCursorPos(center.X, center.Y);
        SendMouseButton(MouseButton.Left, pressed: true);
        SendMouseButton(MouseButton.Left, pressed: false);
        Thread.Sleep(80);
        SendMouseButton(MouseButton.Left, pressed: true);
        SendMouseButton(MouseButton.Left, pressed: false);
    }

    public void Drag((int X, int Y) start, (int X, int Y) end)
    {
        BringToFront();
        SetCursorPos(start.X, start.Y);
        SendMouseButton(MouseButton.Left, pressed: true);
        WaitForWindowMouseCapture(TimeSpan.FromSeconds(1));
        SetCursorPos(start.X + 20, start.Y);
        Thread.Sleep(100);
        SetCursorPos(end.X, end.Y);
        Thread.Sleep(100);
        SendMouseButton(MouseButton.Left, pressed: false);
    }

    private void WaitForWindowMouseCapture(TimeSpan timeout)
    {
        var threadId = GetWindowThreadProcessId(_windowHandle, out _);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var info = new GuiThreadInfo
            {
                Size = Marshal.SizeOf<GuiThreadInfo>()
            };
            if (GetGuiThreadInfo(threadId, ref info) && info.CaptureWindow == _windowHandle)
            {
                return;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("The Desktop Puppy window did not acquire native mouse capture after left-button down.");
    }

    public (int X, int Y) GetPetInteractionPoint()
    {
        var rect = GetWindowRect();
        return (
            (rect.Left + rect.Right) / 2,
            rect.Top + ((rect.Bottom - rect.Top) * 3 / 4));
    }

    public WindowRect GetWindowRect()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = FindVisibleWindow(_process.Id);
        }

        if (_windowHandle == IntPtr.Zero || !GetNativeWindowRect(_windowHandle, out var rect))
        {
            throw new InvalidOperationException("The Desktop Puppy window is no longer available.");
        }

        return new WindowRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public WindowRect WaitForWindowRectChange(WindowRect before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = GetWindowRect();
            if (current.Left != before.Left || current.Top != before.Top)
            {
                return current;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException("The Desktop Puppy window did not move.");
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        return _process.WaitForExit((int)timeout.TotalMilliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }
        }

        _process.Dispose();
    }

    private void BringToFront()
    {
        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = foregroundWindow == IntPtr.Zero
            ? 0u
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThreadId = GetWindowThreadProcessId(_windowHandle, out _);
        var currentThreadId = GetCurrentThreadId();
        var attachedToForeground = foregroundThreadId != 0 &&
                                   foregroundThreadId != currentThreadId &&
                                   AttachThreadInput(currentThreadId, foregroundThreadId, true);
        var attachedToTarget = targetThreadId != 0 &&
                               targetThreadId != currentThreadId &&
                               AttachThreadInput(currentThreadId, targetThreadId, true);
        try
        {
            ShowWindow(_windowHandle, ShowNormal);
            BringWindowToTop(_windowHandle);
            SetForegroundWindow(_windowHandle);
            SetActiveWindow(_windowHandle);
            SetFocus(_windowHandle);
        }
        finally
        {
            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }

            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }

    }

    private static void SendMouseButton(MouseButton button, bool pressed)
    {
        var flags = button switch
        {
            MouseButton.Left when pressed => MouseEventFlags.LeftDown,
            MouseButton.Left => MouseEventFlags.LeftUp,
            MouseButton.Right when pressed => MouseEventFlags.RightDown,
            MouseButton.Right => MouseEventFlags.RightUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };

        var input = new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput { Flags = flags }
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
        {
            throw new InvalidOperationException("SendInput did not inject the mouse event.");
        }
    }

    private static string ReadListItemText(AutomationElement item)
    {
        var textElements = item.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        var parts = textElements.Cast<AutomationElement>()
            .Select(element => element.Current.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return parts.Length == 0 ? item.Current.Name : string.Join(" | ", parts);
    }
    private static int CountVisibleTopLevelWindows(int processId, string automationId)
    {
        var count = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var currentProcessId);
            if (currentProcessId != processId || !IsWindowVisible(handle))
            {
                return true;
            }

            try
            {
                var element = AutomationElement.FromHandle(handle);
                if (element is not null && element.Current.AutomationId == automationId)
                {
                    count++;
                }
            }
            catch (COMException) { }
            catch (ElementNotAvailableException) { }
            return true;
        }, IntPtr.Zero);
        return count;
    }
    private static AutomationElement? FindVisibleTopLevelWindow(int processId, string automationId)
    {
        AutomationElement? found = null;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var currentProcessId);
            if (currentProcessId != processId || !IsWindowVisible(handle))
            {
                return true;
            }

            try
            {
                var element = AutomationElement.FromHandle(handle);
                if (element is not null &&
                    string.Equals(element.Current.AutomationId, automationId, StringComparison.Ordinal))
                {
                    found = element;
                    return false;
                }
            }
            catch (COMException)
            {
                // UIA can race WPF while a top-level window enters the automation tree.
            }
            catch (ElementNotAvailableException)
            {
                // Retry while WPF replaces or closes the top-level window.
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
    private static IntPtr FindVisibleWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var currentProcessId);
            if (currentProcessId != processId || !IsWindowVisible(handle))
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            if (title.ToString() == WindowTitle)
            {
                found = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    private enum MouseButton
    {
        Left,
        Right
    }

    private const uint InputMouse = 0;

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public MouseEventFlags Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public NativeWindowRect CaretRect;
    }

    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    private static extern bool GetGuiThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetNativeWindowRect(IntPtr handle, out NativeWindowRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetActiveWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}

public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public (int X, int Y) Center => ((Left + Right) / 2, (Top + Bottom) / 2);
}
