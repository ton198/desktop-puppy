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
    private const string MissingApplicationMessage = "A current WPF Application is required to use the tray icon host.";

    private ContextMenuStrip? _menu;
    private FormsNotifyIcon? _notifyIcon;
    private Icon? _icon;
    private bool _disposed;
    public string Tooltip { get; init; } = "Desktop Puppy";
    private Action? _notificationClick;
    public void Notify(string text, Action activate)
    {
        VerifyDispatcherAccess();
        if (_disposed || _notifyIcon is null) return;
        _notificationClick = activate;
        _notifyIcon.ShowBalloonTip(8000, "Desktop Puppy", text, ToolTipIcon.Info);
    }

    public event EventHandler<TrayIconCommandEventArgs>? CommandRequested;

    public void Start()
    {
        VerifyDispatcherAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifyIcon is not null) return;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName)
                ?? throw new InvalidOperationException($"Embedded tray icon resource '{IconResourceName}' was not found.");
            _icon = new Icon(stream);
            _menu = TrayContextMenuFactory.Create(OnCommandRequested);
            _notifyIcon = new FormsNotifyIcon();
            _notifyIcon.Icon = _icon;
            _notifyIcon.Text = Tooltip;
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.BalloonTipClicked += NotificationClicked;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        VerifyDispatcherAccess();
        if (_disposed) return;
        _disposed = true;
        CommandRequested = null;

        var menu = _menu;
        var notifyIcon = _notifyIcon;
        var icon = _icon;
        _menu = null;
        _notifyIcon = null;
        _icon = null;
        _notificationClick = null;
        if (notifyIcon is not null) notifyIcon.BalloonTipClicked -= NotificationClicked;

        TrayResourceCleanup.Run(
            () =>
            {
                if (menu is not null) TrayContextMenuFactory.DetachHandlers(menu);
            },
            () =>
            {
                if (notifyIcon is not null) notifyIcon.Visible = false;
            },
            () =>
            {
                if (notifyIcon is not null) notifyIcon.ContextMenuStrip = null;
            },
            () => notifyIcon?.Dispose(),
            () => menu?.Dispose(),
            () => icon?.Dispose());
    }

    private static void VerifyDispatcherAccess()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException(MissingApplicationMessage);
        application.Dispatcher.VerifyAccess();
    }

    private void OnCommandRequested(TrayIconCommand command) =>
        CommandRequested?.Invoke(this, new TrayIconCommandEventArgs(command));
    private void NotificationClicked(object? sender, EventArgs e) => _notificationClick?.Invoke();
}
