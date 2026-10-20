using System.Windows.Forms;
using System.Drawing;

namespace DesktopPuppy.Pet.Tray;

internal static class TrayContextMenuFactory
{
    internal static ContextMenuStrip Create(Action<TrayIconCommand> commandRequested)
    {
        ArgumentNullException.ThrowIfNull(commandRequested);
        var font = new Font("Microsoft YaHei UI", 9f);
        var menu = new ContextMenuStrip
        {
            Tag = commandRequested,
            BackColor = Color.FromArgb(255, 253, 250),
            ForeColor = Color.FromArgb(68, 59, 53),
            Font = font,
            ShowImageMargin = false,
            Padding = new Padding(6),
            Renderer = new ToolStripProfessionalRenderer(new CottageColors()) { RoundedEdges = false }
        };
        menu.Disposed += (_, _) => font.Dispose();
        AddItem(menu, "显示桌宠", TrayIconCommand.ShowPet);
        AddItem(menu, "隐藏桌宠", TrayIconCommand.HidePet);
        AddItem(menu, "暂停 / 恢复", TrayIconCommand.TogglePause);
        AddItem(menu, "说一句", TrayIconCommand.RequestSpeech);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "使用统计", TrayIconCommand.OpenStatistics);
        AddItem(menu, "今日日报", TrayIconCommand.TodayReport);
        AddItem(menu, "日报历史", TrayIconCommand.ReportHistory);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "设置", TrayIconCommand.OpenSettings);
        AddItem(menu, "AI 设置", TrayIconCommand.OpenAiSettings);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "退出桌宠", TrayIconCommand.Exit);
        return menu;
    }

    internal static void DetachHandlers(ContextMenuStrip menu)
    {
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>()) item.Click -= OnItemClick;
        menu.Tag = null;
    }

    private static void AddItem(ContextMenuStrip menu, string text, TrayIconCommand command)
    {
        var item = new ToolStripMenuItem(text) { Tag = command, Padding = new Padding(12, 7, 18, 7) };
        item.Click += OnItemClick;
        menu.Items.Add(item);
    }

    private static void OnItemClick(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: TrayIconCommand command, Owner: ContextMenuStrip { Tag: Action<TrayIconCommand> callback } })
            callback(command);
    }

    private sealed class CottageColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(255, 253, 250);
        public override Color MenuItemSelected => Color.FromArgb(232, 238, 228);
        public override Color MenuItemBorder => Color.FromArgb(173, 193, 165);
        public override Color MenuBorder => Color.FromArgb(229, 220, 209);
        public override Color SeparatorDark => Color.FromArgb(229, 220, 209);
        public override Color SeparatorLight => Color.FromArgb(255, 253, 250);
    }
}
