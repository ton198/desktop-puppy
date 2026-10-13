using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DesktopPuppy.Native;
using DesktopPuppy.Pet.Animation;
using DesktopPuppy.Pet.Placement;

namespace DesktopPuppy.Pet;

public partial class PetWindow
{
    private bool _positioningSpeech;

    private void SetNormalImageLayout(bool? right = null)
    {
        PetImage.Width = PetImage.Height = PetVisualGeometry.ImageSize * _petScale;
        Canvas.SetLeft(PetImage, ((right ?? FacesRight) ? PetVisualGeometry.VisibleBounds.Right - PetVisualGeometry.ImageSize : -PetVisualGeometry.VisibleBounds.Left) * _petScale);
        Canvas.SetTop(PetImage, -PetVisualGeometry.VisibleBounds.Top * _petScale);
    }

    private void SetEdgeImageLayout()
    {
        PetImage.Width = PetImage.Height = Height;
        Canvas.SetLeft(PetImage, (Width - Height) / 2);
        Canvas.SetTop(PetImage, 0);
    }

    private void HideSpeech()
    {
        BubbleBorder.Visibility = Visibility.Collapsed;
        AiNotice.Visibility = Visibility.Collapsed;
        if (_speech.IsVisible) _speech.Hide();
    }

    private void RefreshSpeech()
    {
        if (_closed || !IsVisible) return;
        if (BubbleBorder.Visibility != Visibility.Visible && AiNotice.Visibility != Visibility.Visible)
        {
            _speech.Hide();
            return;
        }
        if (!_speech.IsVisible)
        {
            _speech.Owner = this;
            _speech.Left = Left;
            _speech.Top = Top;
            _speech.Show();
        }
        _speech.UpdateLayout();
        PositionSpeech();
    }

    private void PositionSpeech()
    {
        if (_positioningSpeech || _closed || !_speech.IsVisible || _hwnd == IntPtr.Zero) return;
        if (!_dockWindowApi.TryGetWindowRect(_hwnd, out var pet)) return;
        var handle = new WindowInteropHelper(_speech).Handle;
        if (handle == IntPtr.Zero || _speech.ActualWidth <= 0 || _speech.ActualHeight <= 0) return;
        _positioningSpeech = true;
        try
        {
            var screen = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
            var work = new PhysicalRect(screen.Left, screen.Top, screen.Right, screen.Bottom);
            var dpi = _dockWindowApi.GetDpiForWindow(handle);
            var width = DpiGeometryPolicy.ToPhysicalExtent(_speech.ActualWidth, dpi);
            var height = DpiGeometryPolicy.ToPhysicalExtent(_speech.ActualHeight, dpi);
            var gap = DpiGeometryPolicy.ToPhysicalExtent(6, _dockWindowApi.GetDpiForWindow(_hwnd));
            var target = SpeechPlacementPolicy.Place(pet, width, height, work, gap);
            if (!_dockWindowApi.TryGetWindowRect(handle, out var current) || current != target)
                _dockWindowApi.SetWindowPosition(handle, target);
        }
        finally { _positioningSpeech = false; }
    }
}
