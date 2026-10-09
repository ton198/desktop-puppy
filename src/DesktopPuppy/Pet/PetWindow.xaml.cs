using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using DesktopPuppy.Ai;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using WpfDpiChangedEventArgs = System.Windows.DpiChangedEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfButton = System.Windows.Controls.Button;
using System.Windows.Threading;
using DesktopPuppy.Native;
using DesktopPuppy.Pet.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPuppy.Pet.Interaction;
using DesktopPuppy.Pet.Placement;
using DesktopPuppy.Pet.Tray;

namespace DesktopPuppy.Pet;

public partial class PetWindow : Window, ITrayPetWindow, IRightClickAiView
{
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static readonly string[] LocalLines =
    [
        "我看见你了。别装忙。",
        "点击成功，今天也被我盯上了。",
        "这一下算你赢半秒。",
        "右键是秘密通道，试试看。"
    ];

    private readonly DispatcherTimer _animationTimer;
    private readonly PetAnimationPlayer _animationPlayer = new();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly Dictionary<string, ImageSource> _animationImages = new();
    private TimeSpan _nextIdleAction = TimeSpan.FromSeconds(5);
    private int _specialIndex;
    private int _idleIndex;
    private string _currentFrame = "";
    private readonly DispatcherTimer _interactionTimer;
    private readonly InteractionController _interactionController;
    private readonly PetWindowPlacementService? _placementService;
    private readonly IMonitorDisplayProvider? _monitorDisplayProvider;
    private IntPtr _hwnd;
    private bool _reconcileQueued;
    private bool _closed;
    private int _lineIndex;
    private bool _dragStarted;
    private readonly PetVisibilityState _visibility = new();
    private readonly DispatcherTimer _aiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TimeSpan _noticeDeadline;
    public RightClickAiCoordinator? AiCoordinator { get; set; }
    public Func<bool>? FullscreenNow { get; set; }
    public event EventHandler? AiSettingsRequested;
    public bool CanShowAi => !_closed && IsVisible && _visibility.ShouldShow && !(FullscreenNow?.Invoke() ?? false);
    void IRightClickAiView.ShowPlaceholder() => SetBubbleText("想一句调皮的话……");
    void IRightClickAiView.ShowAiText(string text) => SetBubbleText(text);
    void IRightClickAiView.RemovePlaceholder() => BubbleBorder.Visibility = Visibility.Collapsed;
    public void NotifyAi(string text)
    {
        if (_closed || !IsVisible) return;
        ExpandForInteraction();
        AiNotice.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(AiNotice, text);
        AiNotice.Visibility = Visibility.Visible;
        _noticeDeadline = _animationClock.Elapsed + TimeSpan.FromSeconds(8);
    }
    public void ShowFromUser() { _visibility.SetUserHidden(false); ApplyVisibility(); }
    void ITrayPetWindow.Show() => ShowFromUser();
    void ITrayPetWindow.Hide() { _visibility.SetUserHidden(true); ApplyVisibility(); }
    public void SetFullscreen(bool fullscreen) { _visibility.SetFullscreen(fullscreen); ApplyVisibility(); }
    private void ApplyVisibility()
    {
        if (_closed) return;
        if (_visibility.ShouldShow) { if (!IsVisible) Show(); }
        else { FinishEdgeDrag(); AiCoordinator?.InvalidateBubble(); AiNotice.Visibility = Visibility.Collapsed; if (IsVisible) Hide(); }
    }

    bool ITrayPetWindow.IsMinimized => WindowState == WindowState.Minimized;

    void ITrayPetWindow.Restore() => WindowState = WindowState.Normal;

    void ITrayPetWindow.Activate() { if (_visibility.ShouldShow) _ = Activate(); }

    public event EventHandler? StatisticsRequested;

    public PetWindow(
        PetWindowPlacementService? placementService = null,
        IMonitorDisplayProvider? monitorDisplayProvider = null)
    {
        InitializeComponent();
        foreach (var name in PetAnimationPlayer.Sequences.Values.SelectMany(frames => frames).Select(frame => frame.Name).Append("idle").Distinct())
        {
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Pet/Animation/Assets/{name}.png"));
            // All frames share one crop, preserving alignment between poses.
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(250, 80, 800, 1120));
            cropped.Freeze();
            _animationImages.Add(name, cropped);
        }
        InitializeDocking();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += AnimationTimer_OnTick;
        PlayAnimation("greeting");
        _animationTimer.Start();
        _aiTimer.Tick += (_, _) =>
        {
            AiCoordinator?.Advance();
            if (_animationClock.Elapsed >= _noticeDeadline) AiNotice.Visibility = Visibility.Collapsed;
        };
        _aiTimer.Start();
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
        _animationTimer.Stop();
        _aiTimer.Stop();
        AiCoordinator?.Dispose();
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
            // Startup DPI notifications can precede Loaded, when reconciliation cannot run yet.
            _skipDockOnce = false;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Initial native pet placement failed; keeping WPF startup placement: {exception}");
        }
    }

    private void PetWindow_OnLocationChanged(object? sender, EventArgs e) => QueuePlacementReconcile();

    private void PetWindow_OnDpiChanged(object sender, WpfDpiChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ResetDocking();
        _skipDockOnce = true;
        _dockSuppressedUntilDrag = true;
        QueuePlacementReconcile();
    }

    private void MonitorDisplayProvider_OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ResetDocking();
                _skipDockOnce = true;
                _dockSuppressedUntilDrag = true;
                QueuePlacementReconcile();
            }));
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
                        if (_edgeDock.Side != DesktopPuppy.Pet.Docking.DockSide.None) UpdateDockClip();
                        TryDockAtEdge();
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
        if (e.OriginalSource is WpfButton)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        PrepareDockPointerPress();
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

    private void PetWindow_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_edgeManualDrag)
        {
            if (e.LeftButton == MouseButtonState.Pressed) MoveEdgeDrag();
            else FinishEdgeDrag();
            return;
        }
        if (e.OriginalSource is WpfButton)
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
        if (_edgeManualDrag)
        {
            MoveEdgeDrag();
            FinishEdgeDrag();
            return;
        }
        if (e.OriginalSource is WpfButton)
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
        if (e.OriginalSource is WpfButton)
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
        if (_holdDockForPress && System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed) return;
        _interactionController.Advance(DateTimeOffset.Now);
        SyncInteractionTimer();
    }

    private void OnInteractionEvent(PetInteractionEvent interactionEvent)
    {
        if (interactionEvent.Type != PetInteractionEventType.DragStarted) ExpandForInteraction();
        switch (interactionEvent.Type)
        {
            case PetInteractionEventType.SingleClick:
                PlayAnimation("click");
                ShowBubble(LocalLines[_lineIndex++ % LocalLines.Length]);
                break;
            case PetInteractionEventType.Hit:
                PlayAnimation("hit");
                ShowBubble("嗷！轻点，我的威风差点掉地上。");
                break;
            case PetInteractionEventType.RightClick:
                string[] specials = ["special-tongue", "special-glance", "special-smile", "special-classic"];
                PlayAnimation(specials[_specialIndex++ % specials.Length]);
                if (AiCoordinator is not null) _ = AiCoordinator.RightClickAsync();
                else NotifyAi("AI 服务暂时不可用，请检查 AI 设置。");
                break;
            case PetInteractionEventType.DragStarted:
                _dragStarted = true;
                var manualEdgeDrag = UndockForDrag();
                PlayAnimation("drag");
                _interactionTimer.Stop();
                if (manualEdgeDrag) break;
                ReleaseMouseCapture();
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The window may close while a drag is being initiated.
                }
                finally
                {
                    _interactionController.PointerReleased(interactionEvent.Position, DateTimeOffset.Now);
                    _dragStarted = false;
                    if (!_closed) PlayAnimation("landing");
                    if (!_closed) QueuePlacementReconcile();
                }
                break;
            case PetInteractionEventType.DragCompleted:
                PlayAnimation("landing");
                _dragStarted = false;
                break;
        }
    }

    private void PlayAnimation(string name)
    {
        _animationPlayer.Play(name, _animationClock.Elapsed);
        _nextIdleAction = _animationClock.Elapsed + TimeSpan.FromSeconds(7);
        RenderAnimationFrame();
    }

    private void AnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        if (UpdateDockAnimation()) return;
        RenderAnimationFrame();
        if (!_dragStarted && _edgeDock.Side == DesktopPuppy.Pet.Docking.DockSide.None && _currentFrame == "idle" && _animationClock.Elapsed >= _nextIdleAction)
        {
            string[] idleActions = ["blink", "blink", "happy", "blink", "greeting"];
            PlayAnimation(idleActions[_idleIndex++ % idleActions.Length]);
        }
    }

    private void RenderAnimationFrame()
    {
        var frame = _animationPlayer.FrameAt(_animationClock.Elapsed);
        if (frame == _currentFrame) return;
        _currentFrame = frame;
        PetImage.Source = _animationImages[frame];
        System.Windows.Automation.AutomationProperties.SetName(PetImage, frame);
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
        AiCoordinator?.InvalidateBubble();
        SetBubbleText(text);
    }

    private void SetBubbleText(string text)
    {
        ExpandForInteraction();
        BubbleText.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(BubbleText, text);
        BubbleBorder.Visibility = Visibility.Visible;
    }

    private void BubbleCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        AiCoordinator?.InvalidateBubble();
        BubbleBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void StatisticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatisticsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void AiSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        AiSettingsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
