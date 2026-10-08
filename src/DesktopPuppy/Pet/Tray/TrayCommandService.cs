namespace DesktopPuppy.Pet.Tray;

public sealed class TrayCommandService : IDisposable
{
    private const string StartFailureDiagnostic = "Tray icon initialization failed.";
    private readonly ITrayIconHost _host;
    private readonly ITrayApplicationActions _actions;
    private bool _startAttempted;
    private bool _isAvailable;
    private bool _isDisposed;
    private bool _hostDisposed;
    private bool _exitRequested;

    public TrayCommandService(ITrayIconHost host, ITrayApplicationActions actions)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public bool IsAvailable => _isAvailable;

    public void Start()
    {
        if (_startAttempted || _isDisposed) return;
        _startAttempted = true;
        _host.CommandRequested += OnCommandRequested;
        try
        {
            _host.Start();
            _isAvailable = true;
        }
        catch
        {
            _host.CommandRequested -= OnCommandRequested;
            try { DisposeHostOnce(); }
            catch { /* Host cleanup must not make tray startup fatal. */ }
            TrayDiagnostics.WriteFixed(StartFailureDiagnostic);
        }
    }

    public void ActivateExistingInstance()
    {
        if (!_isDisposed && !_exitRequested) _actions.ShowPet();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isAvailable = false;
        _host.CommandRequested -= OnCommandRequested;
        DisposeHostOnce();
    }

    private void DisposeHostOnce()
    {
        if (_hostDisposed) return;
        _hostDisposed = true;
        _host.Dispose();
    }

    private void OnCommandRequested(object? sender, TrayIconCommandEventArgs e)
    {
        if (!_isAvailable || _isDisposed || _exitRequested) return;
        switch (e.Command)
        {
            case TrayIconCommand.ShowPet: _actions.ShowPet(); break;
            case TrayIconCommand.HidePet: _actions.HidePet(); break;
            case TrayIconCommand.OpenStatistics: _actions.OpenStatistics(); break;
            case TrayIconCommand.OpenAiSettings: _actions.OpenAiSettings(); break;
            case TrayIconCommand.Exit:
                _exitRequested = true;
                _actions.ExitApplication();
                break;
        }
    }
}
