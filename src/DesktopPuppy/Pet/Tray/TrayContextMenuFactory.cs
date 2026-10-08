using System.Windows.Forms;

namespace DesktopPuppy.Pet.Tray;

internal static class TrayContextMenuFactory
{
    internal static ContextMenuStrip Create(Action<TrayIconCommand> commandRequested)
    {
        ArgumentNullException.ThrowIfNull(commandRequested);
        var menu = new ContextMenuStrip { Tag = commandRequested };
        AddItem(menu, "Show Desktop Puppy", TrayIconCommand.ShowPet);
        AddItem(menu, "Hide Desktop Puppy", TrayIconCommand.HidePet);
        AddItem(menu, "Usage Statistics", TrayIconCommand.OpenStatistics);
        AddItem(menu, "AI 设置", TrayIconCommand.OpenAiSettings);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "Exit Desktop Puppy", TrayIconCommand.Exit);
        return menu;
    }

    internal static void DetachHandlers(ContextMenuStrip menu)
    {
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>()) item.Click -= OnItemClick;
        menu.Tag = null;
    }

    private static void AddItem(ContextMenuStrip menu, string text, TrayIconCommand command)
    {
        var item = new ToolStripMenuItem(text) { Tag = command };
        item.Click += OnItemClick;
        menu.Items.Add(item);
    }

    private static void OnItemClick(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: TrayIconCommand command, Owner: ContextMenuStrip { Tag: Action<TrayIconCommand> callback } })
            callback(command);
    }
}
