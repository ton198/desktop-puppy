using DesktopPuppy.Runtime;
using Xunit;
namespace DesktopPuppy.Tests.Runtime;
public sealed class StartupRegistrationTests
{
    [Fact] public void Registry_failure_is_reported_and_does_not_claim_enabled()
    {
        var registry=new MemoryRegistry {FailWrite=true};var service=new StartupRegistrationService(registry,@"C:\Puppy.exe");
        Assert.False(service.TrySetEnabled(true));Assert.NotNull(service.LastError);Assert.False(service.IsEnabled);
    }
    [Fact] public void Disable_removes_only_owned_registry_value_through_adapter()
    {
        var registry=new MemoryRegistry();var service=new StartupRegistrationService(registry,@"C:\Puppy.exe");
        Assert.True(service.TrySetEnabled(true));Assert.True(service.IsEnabled);Assert.True(service.TrySetEnabled(false));Assert.False(service.IsEnabled);
    }
    private sealed class MemoryRegistry:IStartupRegistry
    {
        public bool FailWrite;private string? command;
        public string? Read()=>command;
        public void Write(string? value){if(FailWrite)throw new UnauthorizedAccessException();command=value;}
    }
}
