namespace DesktopPuppy.Runtime;
public sealed class RuntimeState
{
    public bool UserHidden {get;set;}
    public bool Paused {get;set;}
    public bool Fullscreen {get;set;}
    public bool StartupWaiting {get;set;}
    public bool Locked {get;set;}
    public bool Suspended {get;set;}
    public bool ShouldShow=>!UserHidden&&!Fullscreen&&!StartupWaiting;
    public bool CanRequest=>ShouldShow&&!Paused&&!Locked&&!Suspended;
    public bool CanProactive=>CanRequest;
}
public sealed class StartupDelayCoordinator(RuntimeState state,Action show,TimeSpan? delay=null):IDisposable
{
    private readonly CancellationTokenSource cancellation=new();
    private int finished;
    public async Task StartAsync()
    {
        if(Volatile.Read(ref finished)!=0)return;
        state.StartupWaiting=true;
        try {await Task.Delay(delay??TimeSpan.FromSeconds(10),cancellation.Token);Complete();}
        catch(OperationCanceledException){}
    }
    public void ShowNow(){state.UserHidden=false;Complete();cancellation.Cancel();}
    private void Complete(){if(Interlocked.Exchange(ref finished,1)!=0)return;state.StartupWaiting=false;if(state.ShouldShow)show();}
    public void Dispose(){cancellation.Cancel();cancellation.Dispose();}
}
