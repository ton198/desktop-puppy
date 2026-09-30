using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using DesktopPuppy.Pet.Tray;
using Application = System.Windows.Application;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;

namespace DesktopPuppy.Pet.Tray;

public sealed class WinFormsTrayIconHost : ITrayIconHost
{
    private const string IconResourceName = "DesktopPuppy.Pet.Tray.Assets.DesktopPuppyTray.ico";
    private ContextMenuStrip? _menu;
    private FormsNotifyIcon? _notifyIcon;
    private Icon? _icon;
    private bool _disposed;

    public event EventHandler<TrayIconCommandEventArgs>? CommandRequested;

    public void Start()
    {
        Application.Current.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifyIcon is not null) return;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName)
                ?? throw new InvalidOperationException($"Embedded tray icon resource '{IconResourceName}' was not found.");
            _icon = new Icon(stream);
            _menu = TrayContextMenuFactory.Create(OnCommandRequested);
            _notifyIcon = new FormsNotifyIcon
            {
                Icon = _icon,
                Text = "Desktop Puppy",
                ContextMenuStrip = _menu,
                Visible = true
            };
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Application.Current.Dispatcher.VerifyAccess();
        _disposed = true;
        CommandRequested = null;

        if (_menu is not null) TrayContextMenuFactory.DetachHandlers(_menu);
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
    }

    private void OnCommandRequested(TrayIconCommand command) =>
        CommandRequested?.Invoke(this, new TrayIconCommandEventArgs(command));
}
