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

    private readonly PetSpeechWindow _speech = new();
    private Border BubbleBorder => _speech.BubbleBorder;
    private TextBlock BubbleText => _speech.BubbleText;
    private TextBlock AiNotice => _speech.AiNotice;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly DispatcherTimer _dockObserveTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private double _petScale = 1;
    public bool Paused { get; private set; }
    public Func<bool>? RequestBlock { get; set; }
    public bool HasBubble => BubbleBorder.Visibility == Visibility.Visible || AiNotice.Visibility == Visibility.Visible;
    public event Action<PetInteractionEventType>? InteractionTriggered;
    public event Action? PlacementChanged;
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
    private string? _lastVisibilityDiagnostic;
    private readonly DispatcherTimer _aiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TimeSpan _noticeDeadline;
    public RightClickAiCoordinator? AiCoordinator { get; set; }
    public Func<bool>? FullscreenNow { get; set; }
    public bool CanShowAi => !_closed && !Paused && !(RequestBlock?.Invoke() ?? false) && IsVisible && _visibility.ShouldShow && !(FullscreenNow?.Invoke() ?? false);
    public bool UserHidden => _visibility.UserHidden;
    void IRightClickAiView.ShowPlaceholder() => SetBubbleText("想一句调皮的话……");
    void IRightClickAiView.ShowAiText(string text) => SetBubbleText(text);
    void IRightClickAiView.RemovePlaceholder() { BubbleBorder.Visibility = Visibility.Collapsed; RefreshSpeech(); }
    public void NotifyAi(string text)
    {
        if (_closed || !IsVisible) return;
        ExpandForInteraction();
        AiNotice.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(AiNotice, text);
        AiNotice.Visibility = Visibility.Visible;
        _noticeDeadline = _animationClock.Elapsed + TimeSpan.FromSeconds(8);
        RefreshSpeech();
    }
    public void ShowFromUser() { _visibility.SetUserHidden(false); ApplyVisibility(); }
    void ITrayPetWindow.Show() => ShowFromUser();
    void ITrayPetWindow.Hide() { _visibility.SetUserHidden(true); ApplyVisibility(); }
    public void SetFullscreen(bool fullscreen) { _visibility.SetFullscreen(fullscreen); ApplyVisibility(); }
    private void ApplyVisibility()
    {
        if (_closed) return;
        var diagnostic = $"Visibility shouldShow={_visibility.ShouldShow} startupWaiting={_visibility.StartupWaiting} userHidden={_visibility.UserHidden}";
        if (diagnostic != _lastVisibilityDiagnostic)
        {
            _lastVisibilityDiagnostic = diagnostic;
            DesktopPuppy.Runtime.StartupDiagnostics.Write(diagnostic);
        }
        if (_visibility.ShouldShow) { if (!IsVisible) Show(); }
        else { FinishEdgeDrag(); AiCoordinator?.InvalidateBubble(); AiNotice.Visibility = Visibility.Collapsed; if (IsVisible) Hide(); }
    }

    bool ITrayPetWindow.IsMinimized => WindowState == WindowState.Minimized;

    void ITrayPetWindow.Restore() => WindowState = WindowState.Normal;

    void ITrayPetWindow.Activate() { if (_visibility.ShouldShow) _ = Activate(); }


    public PetWindow(
        PetWindowPlacementService? placementService = null,
        IMonitorDisplayProvider? monitorDisplayProvider = null)
    {
        InitializeComponent();
        foreach (var frame in PetVisualGeometry.NormalFrames) _animationImages.Add(frame.Key, frame.Value);
        Width = PetVisualGeometry.VisibleBounds.Width;
        Height = PetVisualGeometry.VisibleBounds.Height;
        SetNormalImageLayout();
        _speech.BubbleClosed += (_, _) => CloseBubble();
        _speech.SizeChanged += (_, _) => PositionSpeech();
        _speech.DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PositionSpeech));
        IsVisibleChanged += (_, _) => { if (!IsVisible) HideSpeech(); else WakeAnimation(); };
        InitializeDocking();
        InitializeFacing();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += AnimationTimer_OnTick;
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            if (_closed || !IsVisible || Paused) return;
            if (_edgeDock.Side != DesktopPuppy.Pet.Docking.DockSide.None) { _edgeBlinkUntil = _animationClock.Elapsed + TimeSpan.FromMilliseconds(120); WakeAnimation(); }
            else if (!_dragStarted && !HasBubble) { string[] actions = ["blink", "blink", "happy", "blink"]; PlayAnimation(actions[_idleIndex++ % actions.Length]); }
            else ScheduleIdle();
        };
        _dockObserveTimer.Tick += (_, _) =>
        {
            if (_closed || !IsVisible || _edgeDock.Side == DesktopPuppy.Pet.Docking.DockSide.None || _animationTimer.IsEnabled) return;
            var inside = DockPointerInside();
            var protect = HasBubble || _holdDockForPress || (_interactionController?.HasPendingSingleClick ?? false);
            if ((inside || protect) != (_edgeDock.Progress <= 0)) WakeAnimation();
        };
        MouseEnter += (_, _) => WakeAnimation();
        MouseLeave += (_, _) => WakeAnimation();
        PlayAnimation("greeting");
        _animationTimer.Start();
        _aiTimer.Tick += (_, _) =>
        {
            AiCoordinator?.Advance();
            if (AiNotice.Visibility == Visibility.Visible && _animationClock.Elapsed >= _noticeDeadline)
            {
                AiNotice.Visibility = Visibility.Collapsed;
                RefreshSpeech();
            }
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
        if (!_exitReady && BeforeExit is not null)
        {
            e.Cancel = true;
            if (!_exitStarted) { _exitStarted = true; _ = CompleteExitAsync(); }
            return;
        }
        _interactionTimer.Stop();
        _animationTimer.Stop();
        _idleTimer.Stop();
        _dockObserveTimer.Stop();
        _aiTimer.Stop();
        AiCoordinator?.Dispose();
        _speech.Close();
        base.OnClosing(e);
    }
    private async Task CompleteExitAsync()
    {
        try { await BeforeExit!(); }
        catch { /* Cleanup is best effort; never expose private exception text. */ }
        finally { _exitReady = true; Close(); }
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
        UpdateFacing();
        _currentFrame = "";
        RenderAnimationFrame();
    }

    private void PetWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        UpdateFacing();
        if (_facing.IsTurning) WakeAnimation();
        QueuePlacementReconcile();
        PositionSpeech();
    }

    private void PetWindow_OnDpiChanged(object sender, WpfDpiChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_restoringPlacement) { SetNormalImageLayout(); PositionSpeech(); return; }
        ResetDocking();
        _skipDockOnce = true;
        _dockSuppressedUntilDrag = true;
        QueuePlacementReconcile();
        PositionSpeech();
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
                PositionSpeech();
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
        if (interactionEvent.Type == PetInteractionEventType.Hit && !_allowHit) return;
        if (interactionEvent.Type is PetInteractionEventType.SingleClick or PetInteractionEventType.Hit or PetInteractionEventType.RightClick)
        {
            InteractionTriggered?.Invoke(interactionEvent.Type);
            if (_soundEnabled) System.Media.SystemSounds.Asterisk.Play();
        }
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
                    if (!_closed) PlacementChanged?.Invoke();
                }
                break;
            case PetInteractionEventType.DragCompleted:
                PlayAnimation("landing");
                _dragStarted = false;
                PlacementChanged?.Invoke();
                break;
        }
    }

    private void PlayAnimation(string name)
    {
        _animationPlayer.Play(name, _animationClock.Elapsed);
        _nextIdleAction = _animationClock.Elapsed + TimeSpan.FromSeconds(7);
        RenderAnimationFrame();
        WakeAnimation();
    }

    private void WakeAnimation()
    {
        if (_closed || !IsVisible) return;
        _idleTimer.Stop();
        _animationTimer.Start();
    }
    private void ScheduleIdle()
    {
        if (_closed || !IsVisible || Paused || _dragStarted) return;
        _idleTimer.Interval = TimeSpan.FromSeconds(7);
        _idleTimer.Start();
    }

    private void AnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible) { _animationTimer.Stop(); return; }
        RecordAnimationTickDiagnostics(!_animationPlayer.NeedsFrames(_animationClock.Elapsed) && !_facing.IsTurning &&
            _edgeDock.State is not (DesktopPuppy.Pet.Docking.DockState.Collapsing or DesktopPuppy.Pet.Docking.DockState.Expanding) &&
            _animationClock.Elapsed >= _edgeBlinkUntil);
        UpdateFacing();
        if (!UpdateDockAnimation()) RenderAnimationFrame();
        var dockTransition = _edgeDock.State is DesktopPuppy.Pet.Docking.DockState.Collapsing or DesktopPuppy.Pet.Docking.DockState.Expanding;
        if (!_animationPlayer.NeedsFrames(_animationClock.Elapsed) && !_facing.IsTurning && !dockTransition && _animationClock.Elapsed >= _edgeBlinkUntil)
        {
            _animationTimer.Stop();
            PublishAnimationDiagnostics();
            ScheduleIdle();
        }
    }

    private void RenderAnimationFrame()
    {
        if (RenderTurnFrame()) return;
        System.Windows.Automation.AutomationProperties.SetItemStatus(PetImage, FacesRight ? "facing-right" : "facing-left");
        var frame = _animationPlayer.FrameAt(_animationClock.Elapsed);
        SetNormalImageLayout();
        if (frame == _currentFrame) return;
        _currentFrame = frame;
        PetImage.Source = FacesRight ? _rightFacingImages[frame] : _animationImages[frame];
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
        RefreshSpeech();
    }

    private void CloseBubble()
    {
        AiCoordinator?.InvalidateBubble();
        BubbleBorder.Visibility = Visibility.Collapsed;
        RefreshSpeech();
    }
}
