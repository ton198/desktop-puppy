using Microsoft.Win32;
namespace DesktopPuppy.Runtime;
public interface IStartupRegistry
{
    string? Read();
    void Write(string? command);
}
public sealed class CurrentUserStartupRegistry:IStartupRegistry
{
    private const string Path=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read(){using var key=Registry.CurrentUser.OpenSubKey(Path);return key?.GetValue("DesktopPuppy") as string;}
    public void Write(string? command){using var key=Registry.CurrentUser.CreateSubKey(Path,true);if(command is null)key.DeleteValue("DesktopPuppy",false);else key.SetValue("DesktopPuppy",command,RegistryValueKind.String);}
}
public sealed class StartupRegistrationService(IStartupRegistry? registry=null,string? executablePath=null)
{
    private readonly IStartupRegistry _registry=registry??new CurrentUserStartupRegistry();
    private readonly string _path=executablePath??Environment.ProcessPath??throw new InvalidOperationException("Executable path unavailable.");
    public string? LastError{get;private set;}
    public bool IsEnabled=>_registry.Read()==BuildCommand(_path);
    public static string BuildCommand(string executable){if(string.IsNullOrWhiteSpace(executable)||executable.Contains('"')||executable.Any(char.IsControl))throw new ArgumentException("Invalid executable path.");return $"\"{executable}\" --startup";}
    public bool TrySetEnabled(bool enabled)
    {
        try{_registry.Write(enabled?BuildCommand(_path):null);LastError=null;return true;}
        catch{LastError="无法更新当前用户开机启动项。";return false;}
    }
}

