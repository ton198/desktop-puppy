using System.Windows.Automation;
using DesktopPuppy.Settings;
using Xunit;
namespace DesktopPuppy.UiTests;
public sealed class CompleteSettingsUiTests
{
    [Fact]
    [Trait("Category","Ui")]
    public async Task Settings_save_persists_size_for_the_running_pet()
    {
        using var seed=new UsageStatisticsSeed();
        var executable=Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");Assert.False(string.IsNullOrWhiteSpace(executable));
        using var harness=DesktopPuppyUiHarness.Start(executable!,seed.DatabasePath);
        harness.WaitForPetWindow(TimeSpan.FromSeconds(5));harness.InvokeTrayMenu("设置");
        var settings=harness.WaitForTopLevelWindow("AiSettingsWindow",TimeSpan.FromSeconds(5));
        var scale=harness.FindByAutomationId(settings,"Scale");((ValuePattern)scale.GetCurrentPattern(ValuePattern.Pattern)).SetValue("150");
        harness.Invoke(harness.FindByAutomationId(settings,"SaveAiSettings"));
        harness.WaitForAutomationName(settings,"AiSettingsStatus",n=>n=="设置已保存。",TimeSpan.FromSeconds(5));
        var service=new SettingsService(seed.DatabasePath);await service.LoadAsync();Assert.Equal(1.5,service.Current.Window.Scale);
    }
}

