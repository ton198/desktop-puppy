using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using DesktopPuppy.Native;
using DesktopPuppy.Pet.Docking;

namespace DesktopPuppy.Pet;

public partial class PetWindow
{
    private readonly EdgeDockController _edgeDock = new();
    private readonly TranslateTransform _edgeOffset = new();
    private readonly Win32WindowApi _dockWindowApi = new();
    private readonly Dictionary<string, ImageSource> _edgeImages = new();
    private Rect _normalInkBounds;
    private MonitorDisplayInfo? _dockMonitor;
    private bool _skipDockOnce;
    private bool _dockSuppressedUntilDrag;
    private bool _holdDockForPress;
    private double _dockPressOffset;
    private bool _edgeManualDrag;
    private CursorPoint _edgePressCursor;
    private PhysicalRect _edgePressWindow;
    private EdgeDragAnchor _edgeDragAnchor;
    private TimeSpan _edgeBlinkUntil;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private void InitializeDocking()
    {
        PetImage.RenderTransform = _edgeOffset;
        LostMouseCapture += (_, _) =>
        {
            FinishEdgeDrag();
        };
        foreach (var side in new[] { "left", "right" })
        foreach (var pose in new[] { "peek", "transition", "blink", "reveal-1", "reveal-2", "reveal-3", "reveal-4", "reveal-5", "reveal-6" })
        {
            var key = $"edge-{pose}-{side}";
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Pet/Animation/Assets/{key}.png"));
            bitmap.Freeze();
            _edgeImages.Add(key, bitmap);
        }
        _normalInkBounds = Animation.PetVisualGeometry.GetInkBounds((BitmapSource)_animationImages["idle"], 128);
    }

    private void TryDockAtEdge()
    {
        if (_skipDockOnce) { _skipDockOnce = false; return; }
        if (_dockSuppressedUntilDrag || _dragStarted || _edgeDock.Side != DockSide.None || !IsVisible || _monitorDisplayProvider is null) return;
        if (!_dockWindowApi.TryGetWindowRect(_hwnd, out var bounds)) return;
        var monitors = _monitorDisplayProvider.GetMonitors();
        var monitor = monitors.OrderByDescending(m =>
            Math.Max(0, Math.Min(m.Bounds.Right, bounds.Right) - Math.Max(m.Bounds.Left, bounds.Left)) *
            (double)Math.Max(0, Math.Min(m.Bounds.Bottom, bounds.Bottom) - Math.Max(m.Bounds.Top, bounds.Top))).FirstOrDefault();
        if (monitor is null) return;
        var visibleLeft = PetImage.PointToScreen(new Point(_normalInkBounds.Left * _petScale, _normalInkBounds.Top * _petScale)).X;
        var visibleRight = PetImage.PointToScreen(new Point(_normalInkBounds.Right * _petScale, _normalInkBounds.Bottom * _petScale)).X;
        var side = EdgeDockController.Detect(visibleLeft, visibleRight, monitor.WorkArea.Left,
            monitor.WorkArea.Right, _dockWindowApi.GetDpiForWindow(_hwnd) / 96d);
        if (side == DockSide.None) return;
        _dockMonitor = monitor;
        _edgeDock.Dock(side, _animationClock.Elapsed);
        // Keep the HWND in this work area; only the clipped image slides across the edge.
        var area = monitor.WorkArea;
        var x = side == DockSide.Left ? area.Left : area.Right - bounds.Width;
        var y = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height));
        if (!_dockWindowApi.SetWindowPosition(_hwnd, new PhysicalRect(x, y, x + bounds.Width, y + bounds.Height)))
        {
            ResetDocking();
            return;
        }
        UpdateDockClip();
        _dockObserveTimer.Start();
        WakeAnimation();
        PlacementChanged?.Invoke();
    }

    private void UpdateDockClip()
    {
        if (_dockMonitor is null) return;
        var a = PointFromScreen(new Point(_dockMonitor.WorkArea.Left, _dockMonitor.WorkArea.Top));
        var b = PointFromScreen(new Point(_dockMonitor.WorkArea.Right, _dockMonitor.WorkArea.Bottom));
        PetRoot.Clip = new RectangleGeometry(new Rect(a, b));
    }

    private bool DockPointerInside()
    {
        if (IsMouseOver) return true;
        if (_edgeDock.State == DockState.Collapsed || !GetCursorPos(out var cursor)) return false;
        // Observe the transparent corridor without intercepting underlying applications' input.
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var origin = PetImage.TranslatePoint(new Point(0, 0), this);
        origin.X -= _edgeOffset.X;
        var left = _edgeDock.Side == DockSide.Left ? 0 : origin.X;
        var right = _edgeDock.Side == DockSide.Right ? ActualWidth : origin.X + PetImage.ActualWidth;
        return point.X >= left && point.X <= right && point.Y >= origin.Y && point.Y <= origin.Y + PetImage.ActualHeight;
    }

    private bool UpdateDockAnimation()
    {
        if (_edgeDock.Side == DockSide.None || _dragStarted) return false;
        if (_holdDockForPress) return true;
        var protect = BubbleBorder.Visibility == Visibility.Visible || AiNotice.Visibility == Visibility.Visible ||
            System.Windows.Input.Mouse.Captured == this || _interactionController.HasPendingSingleClick;
        _edgeDock.Update(_animationClock.Elapsed, DockPointerInside(), protect);
        var progress = _edgeDock.Progress;
        if (progress > 0) SetRevealImageLayout(progress);
        else SetNormalImageLayout();
        var origin = PetImage.TranslatePoint(new Point(0, 0), this);
        origin.X -= _edgeOffset.X;
        var shift = _edgeDock.Side == DockSide.Left ? -(origin.X + PetImage.Width * 850 / 1254) :
            ActualWidth - origin.X - PetImage.Width * (1254 - 850) / 1254;
        _edgeOffset.X = shift * progress;
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, $"dock-{_edgeDock.Side}-{_edgeDock.State}");
        if (progress <= 0) { SetNormalImageLayout(); return false; }
        var frame = progress >= .98 ?
            $"edge-{(_animationClock.Elapsed < _edgeBlinkUntil ? "blink" : "peek")}-{_edgeDock.Side.ToString().ToLowerInvariant()}" :
            EdgeRevealPose.Frame(_edgeDock.Side, progress);
        _currentFrame = frame;
        PetImage.Source = _edgeImages[frame];
        System.Windows.Automation.AutomationProperties.SetName(PetImage, frame);
        return true;
    }

    private void SetRevealImageLayout(double progress)
    {
        var blend = EdgeRevealPose.LayoutBlend(progress);
        var normalSize = Animation.PetVisualGeometry.ImageSize * _petScale;
        var normalBounds = Animation.PetVisualGeometry.VisibleBounds;
        var normalLeft = (FacesRight ? normalBounds.Right - Animation.PetVisualGeometry.ImageSize : -normalBounds.Left) * _petScale;
        PetImage.Width = PetImage.Height = normalSize + (Height - normalSize) * blend;
        System.Windows.Controls.Canvas.SetLeft(PetImage, normalLeft + ((Width - Height) / 2 - normalLeft) * blend);
        System.Windows.Controls.Canvas.SetTop(PetImage, -normalBounds.Top * _petScale * (1 - blend));
    }

    private void ExpandForInteraction()
    {
        _holdDockForPress = false;
        _dockPressOffset = 0;
        _edgeDock.KeepExpanded(_animationClock.Elapsed);
        _edgeOffset.X = 0;
        SetNormalImageLayout();
        RenderAnimationFrame();
        WakeAnimation();
    }

    private void ResetDocking()
    {
        _holdDockForPress = false;
        _dockPressOffset = 0;
        _edgeDock.Reset();
        _dockObserveTimer.Stop();
        _dockMonitor = null;
        _edgeOffset.X = 0;
        PetRoot.Clip = null;
        SetNormalImageLayout();
        _currentFrame = "";
        RenderAnimationFrame();
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, "dock-None-Free");
    }

    private void PrepareDockPointerPress()
    {
        if (_edgeDock.Side != DockSide.None && _edgeDock.Progress > 0)
        {
            // Freeze the transformed pose until the click/drag decision, preserving the grabbed point.
            _holdDockForPress = true;
            _dockPressOffset = _edgeOffset.X;
            GetCursorPos(out _edgePressCursor);
            _dockWindowApi.TryGetWindowRect(_hwnd, out _edgePressWindow);
            var origin = PetImage.TranslatePoint(new Point(0, 0), this);
            var normal = Animation.PetVisualGeometry.VisibleBounds;
            var normalLeft = (FacesRight ? normal.Right - Animation.PetVisualGeometry.ImageSize : -normal.Left) * _petScale;
            _edgeDragAnchor = EdgeDragAnchor.FromPose(_edgePressCursor.X, _edgePressCursor.Y, _edgePressWindow,
                _dockWindowApi.GetDpiForWindow(_hwnd) / 96d, origin.X, origin.Y, PetImage.Width,
                normalLeft, -normal.Top * _petScale, Animation.PetVisualGeometry.ImageSize * _petScale);
            if (_diagnosticsEnabled)
                System.Windows.Automation.AutomationProperties.SetHelpText(this,System.Text.Json.JsonSerializer.Serialize(new
                { grabXDip=_edgeDragAnchor.GrabXDip,grabYDip=_edgeDragAnchor.GrabYDip,pressX=_edgePressCursor.X,pressY=_edgePressCursor.Y,poseX=origin.X,poseY=origin.Y,poseSize=PetImage.Width }));
        }
        else ExpandForInteraction();
    }

    private bool UndockForDrag()
    {
        if (_holdDockForPress && _edgePressWindow.IsValid)
        {
            _edgeManualDrag = true;
        }
        _dockSuppressedUntilDrag = false;
        ResetDocking();
        MoveEdgeDrag();
        return _edgeManualDrag;
    }

    private void MoveEdgeDrag()
    {
        if (!_edgeManualDrag || !GetCursorPos(out var cursor)) return;
        if (!_dockWindowApi.TryGetWindowRect(_hwnd, out var current)) return;
        _dockWindowApi.SetWindowPosition(_hwnd, _edgeDragAnchor.At(cursor.X, cursor.Y,
            _dockWindowApi.GetDpiForWindow(_hwnd) / 96d, current.Width, current.Height));
    }

    private void FinishEdgeDrag()
    {
        if (!_edgeManualDrag) return;
        _edgeManualDrag = false;
        _dragStarted = false;
        _dockSuppressedUntilDrag = false;
        _interactionController.PointerReleased(new DesktopPuppy.Pet.Interaction.PointerPoint(0, 0), DateTimeOffset.Now);
        if (System.Windows.Input.Mouse.Captured == this) ReleaseMouseCapture();
        if (!_closed) QueuePlacementReconcile();
        if (!_closed) PlacementChanged?.Invoke();
    }
}
