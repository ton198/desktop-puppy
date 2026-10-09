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
        foreach (var pose in new[] { "peek", "transition", "blink" })
        {
            var key = $"edge-{pose}-{side}";
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Pet/Animation/Assets/{key}.png"));
            bitmap.Freeze();
            _edgeImages.Add(key, bitmap);
        }
        // Ignore the transparent padding when deciding whether the pet is near an edge.
        var source = (BitmapSource)_animationImages["idle"];
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        var left = bgra.PixelWidth; var top = bgra.PixelHeight; var right = 0; var bottom = 0;
        for (var y = 0; y < bgra.PixelHeight; y++)
        for (var x = 0; x < bgra.PixelWidth; x++)
        {
            if (pixels[(y * bgra.PixelWidth + x) * 4 + 3] <= 128) continue;
            left = Math.Min(left, x); right = Math.Max(right, x + 1);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1);
        }
        var scale = Math.Min(PetImage.Width / bgra.PixelWidth, PetImage.Height / bgra.PixelHeight);
        _normalInkBounds = new Rect((PetImage.Width - bgra.PixelWidth * scale) / 2 + left * scale,
            (PetImage.Height - bgra.PixelHeight * scale) / 2 + top * scale,
            (right - left) * scale, (bottom - top) * scale);
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
        var visibleLeft = PetImage.PointToScreen(_normalInkBounds.TopLeft).X;
        var visibleRight = PetImage.PointToScreen(_normalInkBounds.BottomRight).X;
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
        var origin = PetImage.TranslatePoint(new Point(0, 0), this);
        origin.X -= _edgeOffset.X;
        var shift = _edgeDock.Side == DockSide.Left ? -(origin.X + PetImage.Width * 850 / 1254) :
            ActualWidth - origin.X - PetImage.Width * (1254 - 850) / 1254;
        _edgeOffset.X = shift * progress;
        PetChrome.Visibility = progress > 0 ? Visibility.Hidden : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, $"dock-{_edgeDock.Side}-{_edgeDock.State}");
        if (progress <= 0) return false;
        var pose = progress < .8 ? "transition" :
            _animationClock.Elapsed.TotalMilliseconds % 7000 < 120 ? "blink" : "peek";
        var frame = $"edge-{pose}-{_edgeDock.Side.ToString().ToLowerInvariant()}";
        _currentFrame = frame;
        PetImage.Source = _edgeImages[frame];
        System.Windows.Automation.AutomationProperties.SetName(PetImage, frame);
        return true;
    }

    private void ExpandForInteraction()
    {
        _holdDockForPress = false;
        _dockPressOffset = 0;
        _edgeDock.KeepExpanded(_animationClock.Elapsed);
        _edgeOffset.X = 0;
        PetChrome.Visibility = Visibility.Visible;
        RenderAnimationFrame();
    }

    private void ResetDocking()
    {
        _holdDockForPress = false;
        _dockPressOffset = 0;
        _edgeDock.Reset();
        _dockMonitor = null;
        _edgeOffset.X = 0;
        PetRoot.Clip = null;
        PetChrome.Visibility = Visibility.Visible;
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
        }
        else ExpandForInteraction();
    }

    private bool UndockForDrag()
    {
        if (_holdDockForPress && _edgePressWindow.IsValid)
        {
            var scale = _dockWindowApi.GetDpiForWindow(_hwnd) / 96d;
            var shift = (int)Math.Round(_dockPressOffset * scale);
            _edgePressWindow = new PhysicalRect(_edgePressWindow.Left + shift, _edgePressWindow.Top,
                _edgePressWindow.Right + shift, _edgePressWindow.Bottom);
            _edgeDragAnchor = EdgeDragAnchor.FromPress(_edgePressCursor.X, _edgePressCursor.Y, _edgePressWindow, scale);
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
    }
}
