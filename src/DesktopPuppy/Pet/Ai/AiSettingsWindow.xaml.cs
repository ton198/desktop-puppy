using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using DesktopPuppy.Ai;
using DesktopPuppy.Security;

namespace DesktopPuppy.Pet.Ai;

public partial class AiSettingsWindow : Window
{
    private readonly AiSqliteStore _store;
    private readonly ApiKeyService _keys;
    public AiSettingsWindow(AiSqliteStore store, ApiKeyService keys)
    {
        _store = store; _keys = keys; InitializeComponent();
        Loaded += async (_, _) => await OperateAsync(async () =>
        {
            var configuration = await Task.Run(_store.LoadSettings);
            AiEnabled.IsChecked = configuration.Enabled; RightClickEnabled.IsChecked = configuration.RightClickEnabled;
            await RefreshStatusesAsync();
        });
        Closed += (_, _) => { DeepSeekKey.Clear(); JevKey.Clear(); };
    }
    private async Task OperateAsync(Func<Task> action)
    {
        SettingsPanel.IsEnabled = false; StatusText.Text = "";
        try { await action(); }
        catch { StatusText.Text = "操作失败，请检查配置或稍后再试。"; }
        finally { SettingsPanel.IsEnabled = true; }
    }
    private async Task RefreshStatusesAsync()
    {
        var configured = await Task.Run(() => (_keys.IsConfigured(AiProvider.DeepSeek), _keys.IsConfigured(AiProvider.Jev)));
        DeepSeekStatus.Text = configured.Item1 ? "已保存（不回显）" : "未配置";
        JevStatus.Text = configured.Item2 ? "已保存（不回显）" : "未配置";
    }
    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var configuration = new AiConfiguration(AiEnabled.IsChecked == true, RightClickEnabled.IsChecked == true);
        await OperateAsync(async () => { await Task.Run(() => _store.SaveSettings(configuration)); StatusText.Text = "设置已保存。"; });
    }
    private async void SaveKey_OnClick(object sender, RoutedEventArgs e)
    {
        var provider = Enum.Parse<AiProvider>((string)((Button)sender).Tag);
        var input = provider == AiProvider.DeepSeek ? DeepSeekKey : JevKey; var key = input.Password; input.Clear();
        await OperateAsync(async () => { await Task.Run(() => _keys.Save(provider, key)); await RefreshStatusesAsync(); StatusText.Text = "Key 已加密保存。"; });
    }
    private async void DeleteKey_OnClick(object sender, RoutedEventArgs e)
    {
        var provider = Enum.Parse<AiProvider>((string)((Button)sender).Tag);
        await OperateAsync(async () => { await Task.Run(() => _keys.Delete(provider)); await RefreshStatusesAsync(); StatusText.Text = "Key 已删除。"; });
    }
}
