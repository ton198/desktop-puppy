using System.Windows.Automation;
using DesktopPuppy.Ai;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class AiSettingsWindowUiTests
{
    [Fact]
    [Trait("Category","Ui")]
    public void Ai_settings_opens_once_and_persists_switch_without_showing_keys()
    {
        using var seed=new UsageStatisticsSeed();
        var executable=Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        using var harness=DesktopPuppyUiHarness.Start(executable!,seed.DatabasePath);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.InvokeTrayMenu("AI 设置");
        var settings=harness.WaitForTopLevelWindow("AiSettingsWindow",TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(settings,"DeepSeekKeyStatus",name=>name=="未配置",TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(settings,"JevKeyStatus",name=>name=="未配置",TimeSpan.FromSeconds(5));
        var input=harness.FindByAutomationId(settings,"DeepSeekKeyInput"); Assert.True(input.Current.IsPassword);
        var enabled=harness.FindByAutomationId(settings,"AiEnabled");
        ((TogglePattern)enabled.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        harness.Invoke(harness.FindByAutomationId(settings,"SaveAiSettings"));
        harness.WaitForAutomationName(settings,"AiSettingsStatus",name=>name=="设置已保存。",TimeSpan.FromSeconds(5));
        Assert.False(new AiSqliteStore(seed.DatabasePath).LoadSettings().Enabled);
        harness.InvokeTrayMenu("AI 设置");
        Assert.Equal(1,harness.CountTopLevelWindows("AiSettingsWindow"));
    }

    [Fact]
    [Trait("Category","Ui")]
    public void Local_single_click_still_has_closable_bubble()
    {
        var executable=Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE"); Assert.False(string.IsNullOrWhiteSpace(executable));
        using var harness=DesktopPuppyUiHarness.Start(executable!);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(5)); harness.WaitForAutomationName(pet,"PetAnimation",n=>n=="idle",TimeSpan.FromSeconds(4)); harness.SingleClickCenter();
        var original = harness.WaitForAutomationId(harness.WaitForSpeechWindow(),"SpeechBubbleText",TimeSpan.FromSeconds(3)).Current.Name;
        harness.RightClickCenter();
        harness.WaitForAutomationName(harness.WaitForSpeechWindow(),"AiNotice",name=>name.Contains("配置"),TimeSpan.FromSeconds(5));
        Assert.Equal(original, harness.FindByAutomationId(harness.WaitForSpeechWindow(),"SpeechBubbleText").Current.Name);
        harness.Invoke(harness.FindByAutomationId(harness.WaitForSpeechWindow(),"CloseBubbleButton"));
        harness.WaitForAutomationIdToDisappear(harness.WaitForSpeechWindow(),"SpeechBubbleText",TimeSpan.FromSeconds(2));
    }
}
