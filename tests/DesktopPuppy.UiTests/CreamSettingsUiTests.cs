using System.Windows.Automation;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class CreamSettingsUiTests
{
    [Fact, Trait("Category", "Ui")]
    public async Task Navigation_preserves_edits_and_save_stays_available_on_long_pages()
    {
        using var seed = new UsageStatisticsSeed();
        using var harness = DesktopPuppyUiHarness.Start(Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE")!, seed.DatabasePath);
        harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        harness.InvokeTrayMenu("设置");
        var window = harness.WaitForTopLevelWindow("AiSettingsWindow", TimeSpan.FromSeconds(5));
        var scale = harness.FindByAutomationId(window, "Scale");
        ((ValuePattern)scale.GetCurrentPattern(ValuePattern.Pattern)).SetValue("125");
        var tab = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "统计与隐私"));
        ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        var save = harness.FindByAutomationId(window, "SaveAiSettings");
        Assert.False(save.Current.IsOffscreen);
        harness.Invoke(save);
        harness.WaitForAutomationName(window, "AiSettingsStatus", n => n == "设置已保存。", TimeSpan.FromSeconds(5));
        var settings = new DesktopPuppy.Settings.SettingsService(seed.DatabasePath);
        await settings.LoadAsync();
        Assert.Equal(1.25, settings.Current.Window.Scale);
        harness.InvokeTrayMenu("AI 设置");
        harness.WaitForAutomationId(window, "DeepSeekKeyInput", TimeSpan.FromSeconds(5));
        Assert.False(harness.FindByAutomationId(window, "SaveAiSettings").Current.IsOffscreen);
    }
}
