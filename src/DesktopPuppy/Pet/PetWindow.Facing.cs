using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using DesktopPuppy.Pet.Animation;
using DesktopPuppy.Pet.Docking;

namespace DesktopPuppy.Pet;

public partial class PetWindow
{
    private readonly PetFacingController _facing = new();
    private readonly Dictionary<string, ImageSource> _rightFacingImages = new();
    private bool FacesRight => _edgeDock.Side == DockSide.Left ||
        (_edgeDock.Side == DockSide.None && _facing.FacingRight);

    private void InitializeFacing()
    {
        foreach (var (key, image) in _animationImages)
        {
            var mirror = new TransformedBitmap((BitmapSource)image, new ScaleTransform(-1, 1));
            mirror.Freeze();
            _rightFacingImages.Add(key, mirror);
        }
        for (var i = 1; i <= 3; i++)
        {
            var key = $"turn-{i}";
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Pet/Animation/Assets/{key}.png"));
            bitmap.Freeze();
            _edgeImages.Add(key, bitmap);
        }
    }

    private void UpdateFacing()
    {
        if (!IsLoaded || _monitorDisplayProvider is null || !_dockWindowApi.TryGetWindowRect(_hwnd, out var bounds)) return;
        var monitor = _monitorDisplayProvider.GetMonitors().OrderByDescending(m =>
            Math.Max(0, Math.Min(m.Bounds.Right, bounds.Right) - Math.Max(m.Bounds.Left, bounds.Left)) *
            (double)Math.Max(0, Math.Min(m.Bounds.Bottom, bounds.Bottom) - Math.Max(m.Bounds.Top, bounds.Top))).FirstOrDefault();
        if (monitor is null) return;
        var old = _facing.FacingRight;
        _facing.Update((bounds.Left + bounds.Right) / 2d,
            (monitor.WorkArea.Left + monitor.WorkArea.Right) / 2d,
            16 * _dockWindowApi.GetDpiForWindow(_hwnd) / 96d, _animationClock.Elapsed);
        if (old != _facing.FacingRight) _currentFrame = "";
    }

    private bool RenderTurnFrame()
    {
        if (!_facing.IsTurning || _edgeDock.Progress > 0 || _holdDockForPress) return false;
        var orientation = _facing.Orientation;
        // Near either endpoint use the exact normal artwork, keeping the handoff seamless.
        if (orientation < .12 || orientation > .88)
        {
            var right = orientation < .5;
            SetNormalImageLayout(right);
            PetImage.Source = right ? _rightFacingImages["idle"] : _animationImages["idle"];
            _currentFrame = right ? "turn-right" : "turn-left";
        }
        else
        {
            var index = orientation < .35 ? 1 : orientation < .65 ? 2 : 3;
            _currentFrame = $"turn-{index}";
            PetImage.Width = PetImage.Height = PetVisualGeometry.ImageSize * _petScale;
            var bounds = PetVisualGeometry.VisibleBounds;
            Canvas.SetLeft(PetImage, ((bounds.Right - PetVisualGeometry.ImageSize) * (1 - orientation) - bounds.Left * orientation) * _petScale);
            Canvas.SetTop(PetImage, -bounds.Top * _petScale);
            PetImage.Source = _edgeImages[_currentFrame];
        }
        System.Windows.Automation.AutomationProperties.SetItemStatus(PetImage, "turning");
        System.Windows.Automation.AutomationProperties.SetName(PetImage, _currentFrame);
        return true;
    }
}
