using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPuppy.Native;
using DesktopPuppy.Pet.Interaction;
using DesktopPuppy.Pet.Placement;

namespace DesktopPuppy.Pet;

public partial class PetWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static readonly string[] LocalLines =
    [
        "我看见你了。别装忙。",
        "点击成功，今天也被我盯上了。",
        "这一下算你赢半秒。",
        "右键是秘密通道，暂时离线。"
    ];

    private readonly DispatcherTimer _interactionTimer;
    private readonly InteractionController _interactionController;
    private readonly PetWindowPlacementService? _placementService;
    private readonly IMonitorDisplayProvider? _monitorDisplayProvider;
    private IntPtr _hwnd;
    private bool _reconcileQueued;
    private bool _closed;
    private int _lineIndex;
    private bool _dragStarted;

    public event EventHandler? StatisticsRequested;

    public PetWindow(
        PetWindowPlacementService? placementService = null,
        IMonitorDisplayProvider? monitorDisplayProvider = null)
    {
        InitializeComponent();
        _placementService = placementService;
        _monitorDisplayProvider = monitorDisplayProvider;

        SourceInitialized += PetWindow_OnSourceInitialized;
        Loaded += PetWindow_OnLoaded;
        LocationChanged += PetWindow_OnLocationChanged;
        DpiChanged += PetWindow_OnDpiChanged;
        if (_monitorDisplayProvider is not null)
        {
            _monitorDisplayProvider.DisplaySettingsChanged += MonitorDisplayProvider_OnDisplaySettingsChanged;
        }

        _interactionController = new InteractionController(
            new PetInteractionPolicy(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMilliseconds(GetDoubleClickTime()),
            dragThreshold: 8,
            OnInteractionEvent);

        _interactionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _interactionTimer.Tick += InteractionTimer_OnTick;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _interactionTimer.Stop();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_monitorDisplayProvider is not null)
        {
            _monitorDisplayProvider.DisplaySettingsChanged -= MonitorDisplayProvider_OnDisplaySettingsChanged;
        }

        if (_hwnd != IntPtr.Zero)
        {
            try
            {
                _placementService?.Forget(_hwnd);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Pet window placement cleanup failed: {exception}");
            }
        }

        base.OnClosed(e);
    }

    private void PetWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
    }

    private void PetWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_placementService is null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _placementService.ApplyInitial(_hwnd, ActualWidth, ActualHeight);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Initial native pet placement failed; keeping WPF startup placement: {exception}");
        }
    }

    private void PetWindow_OnLocationChanged(object? sender, EventArgs e) => QueuePlacementReconcile();

    private void PetWindow_OnDpiChanged(object sender, DpiChangedEventArgs e) => QueuePlacementReconcile();

    private void MonitorDisplayProvider_OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(QueuePlacementReconcile));
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine($"Display-change notification could not be dispatched during shutdown: {exception}");
        }
    }

    private void QueuePlacementReconcile()
    {
        if (_closed || !IsLoaded || _hwnd == IntPtr.Zero || _placementService is null || _reconcileQueued ||
            Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _reconcileQueued = true;
        try
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _reconcileQueued = false;
                    if (_closed || !IsLoaded || _hwnd == IntPtr.Zero)
                    {
                        return;
                    }

                    try
                    {
                        _placementService.Reconcile(_hwnd, ActualWidth, ActualHeight);
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine($"Native pet placement reconciliation failed; preserving the current WPF position: {exception}");
                    }
                }));
        }
        catch (InvalidOperationException exception)
        {
            _reconcileQueued = false;
            Debug.WriteLine($"Pet placement reconciliation could not be dispatched during shutdown: {exception}");
        }
    }

    private void PetWindow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        _interactionController.PointerPressed(
            new PointerPoint(e.GetPosition(this).X, e.GetPosition(this).Y),
            now,
            e.ClickCount);
        SyncInteractionTimer();

        if (e.ClickCount == 1)
        {
            CaptureMouse();
        }
    }

    private void PetWindow_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var point = e.GetPosition(this);
        _interactionController.PointerMoved(
            new PointerPoint(point.X, point.Y),
            DateTimeOffset.Now,
            e.LeftButton == MouseButtonState.Pressed);
        SyncInteractionTimer();
    }

    private void PetWindow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var point = e.GetPosition(this);
        _interactionController.PointerReleased(
            new PointerPoint(point.X, point.Y),
            DateTimeOffset.Now);
        SyncInteractionTimer();
        if (!_dragStarted)
        {
            ReleaseMouseCapture();
        }

        _dragStarted = false;
    }

    private void PetWindow_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        _interactionController.RightClicked(
            DateTimeOffset.Now,
            new PointerPoint(e.GetPosition(this).X, e.GetPosition(this).Y));
        e.Handled = true;
    }

    private void InteractionTimer_OnTick(object? sender, EventArgs e)
    {
        _interactionController.Advance(DateTimeOffset.Now);
        SyncInteractionTimer();
    }

    private void OnInteractionEvent(PetInteractionEvent interactionEvent)
    {
        switch (interactionEvent.Type)
        {
            case PetInteractionEventType.SingleClick:
                ShowBubble(LocalLines[_lineIndex++ % LocalLines.Length]);
                break;
            case PetInteractionEventType.Hit:
                ShowBubble("嗷！轻点，我还在离线模式里努力。");
                break;
            case PetInteractionEventType.RightClick:
                ShowBubble(LocalLines[^1]);
                break;
            case PetInteractionEventType.DragStarted:
                _dragStarted = true;
                _interactionTimer.Stop();
                ReleaseMouseCapture();
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The window may close while a drag is being initiated.
                }
                break;
            case PetInteractionEventType.DragCompleted:
                _dragStarted = false;
                break;
        }
    }

    private void SyncInteractionTimer()
    {
        if (_interactionController.HasPendingSingleClick)
        {
            if (!_interactionTimer.IsEnabled)
            {
                _interactionTimer.Start();
            }
        }
        else
        {
            _interactionTimer.Stop();
        }
    }

    private void ShowBubble(string text)
    {
        BubbleText.Text = text;
        BubbleBorder.Visibility = Visibility.Visible;
    }

    private void BubbleCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        BubbleBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void StatisticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatisticsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
