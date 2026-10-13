using System.Windows;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Pet.Docking;
using DesktopPuppy.Settings;
using DesktopPuppy.Native;

namespace DesktopPuppy.Pet;

public partial class PetWindow
{
    private bool _allowHit = true, _soundEnabled, _exitReady, _exitStarted;
    private bool _restoringPlacement;
    public Func<Task>? BeforeExit { get; set; }
    public void SetStartupWaiting(bool value) { _visibility.StartupWaiting = value; ApplyVisibility(); }
    public void SetPaused(bool paused)
    {
        Paused = paused;
        if (paused) { AiCoordinator?.InvalidateBubble(); _idleTimer.Stop(); }
        else ScheduleIdle();
    }
    public void ApplySettings(AppSettings settings)
    {
        var changedSize = _petScale != settings.Window.Scale;
        _petScale = settings.Window.Scale;
        _allowHit = settings.Interaction.AllowHit;
        _soundEnabled = settings.Interaction.SoundEnabled;
        Topmost = settings.Window.Topmost;
        Opacity = settings.Window.Opacity;
        _speech.Opacity = Opacity;
        _speech.Topmost = Topmost;
        if (changedSize)
        {
            ResetDocking();
            Width = Animation.PetVisualGeometry.VisibleBounds.Width * _petScale;
            Height = Animation.PetVisualGeometry.VisibleBounds.Height * _petScale;
            SetNormalImageLayout();
            QueuePlacementReconcile();
            PositionSpeech();
        }
    }
    public void PlayProactive(string animation) { if (!_closed) { ExpandForInteraction(); PlayAnimation(animation); } }
    public SavedPetPlacement? CapturePlacement()
    {
        if (_hwnd == IntPtr.Zero || _monitorDisplayProvider is null || !_dockWindowApi.TryGetWindowRect(_hwnd, out var bounds)) return null;
        var monitor = _monitorDisplayProvider.GetMonitors().OrderByDescending(m => Math.Max(0, Math.Min(m.Bounds.Right,bounds.Right)-Math.Max(m.Bounds.Left,bounds.Left)) *
            (double)Math.Max(0,Math.Min(m.Bounds.Bottom,bounds.Bottom)-Math.Max(m.Bounds.Top,bounds.Top))).FirstOrDefault();
        if (monitor is null) return null;
        var scale = _dockWindowApi.GetDpiForWindow(_hwnd) / 96d;
        return new(monitor.DeviceId, (bounds.Left-monitor.WorkArea.Left)/scale,
            (bounds.Top-monitor.WorkArea.Top)/scale, _edgeDock.Side.ToString());
    }
    public void RestorePlacement(SavedPetPlacement? saved)
    {
        if (_hwnd == IntPtr.Zero || _monitorDisplayProvider is null) return;
        if (saved is null)
        {
            ResetPosition();
            _dockSuppressedUntilDrag=false;
            return;
        }
        var monitors = _monitorDisplayProvider.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.DeviceId == saved.MonitorId) ?? monitors.First(m => m.IsPrimary);
        var scale = MonitorDpi.At(monitor.WorkArea, _dockWindowApi.GetDpiForWindow(_hwnd)) / 96d;
        var width = (int)Math.Round(Width*scale); var height=(int)Math.Round(Height*scale);
        var area = monitor.WorkArea;
        var left = (int)Math.Round(area.Left+saved.LeftDip*scale);
        var top = (int)Math.Round(area.Top+saved.TopDip*scale);
        if (saved.DockSide == "Left") left=area.Left;
        if (saved.DockSide == "Right") left=area.Right-width;
        left=Math.Clamp(left,area.Left-width+20,area.Right-20);
        top=Math.Clamp(top,area.Top-height+20,area.Bottom-20);
        _restoringPlacement = true;
        _dockWindowApi.SetWindowPosition(_hwnd,new PhysicalRect(left,top,left+width,top+height));
        UpdateFacing();
        _skipDockOnce=false; _dockSuppressedUntilDrag=saved.DockSide=="None";
        QueuePlacementReconcile(); WakeAnimation();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        { _restoringPlacement=false; _skipDockOnce=false; TryDockAtEdge(); WakeAnimation(); }));
    }
    public void ResetPosition()
    {
        ResetDocking();
        var area=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var physical = new PhysicalRect(area.Left,area.Top,area.Right,area.Bottom);
        var scale = MonitorDpi.At(physical,_dockWindowApi.GetDpiForWindow(_hwnd)) / 96d;
        var width=(int)Math.Round(Width*scale);var height=(int)Math.Round(Height*scale);
        var left=area.Left+(area.Width-width)/2;var top=area.Top+(area.Height-height)/2;
        _dockWindowApi.SetWindowPosition(_hwnd,new PhysicalRect(left,top,left+width,top+height));
        _dockSuppressedUntilDrag=true;
        QueuePlacementReconcile(); WakeAnimation();
    }
}
