using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using DesktopPuppy.Pet.Tray;
using Xunit;

namespace DesktopPuppy.Tests.Pet.Tray;

public sealed class TrayContextMenuFactoryTests
{
    // Purpose: Keep the tray menu labels and order aligned with the approved product menu.
    // Coverage: Build the real factory menu on an STA thread and inspect its actual items.
    // Target effect: Users see Show, Hide, Statistics, a separator, and Exit in that order.
    [Fact]
    public void Menu_has_approved_labels_and_order()
    {
        RunOnSta(() =>
        {
            using var menu = TrayContextMenuFactory.Create(_ => { });
            Assert.Equal(
                ["Show Desktop Puppy", "Hide Desktop Puppy", "Usage Statistics", "AI 设置", "<separator>", "Exit Desktop Puppy"],
                menu.Items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "<separator>" : item.Text));
        });
    }

    // Purpose: Ensure each menu action requests only its corresponding tray command.
    // Coverage: PerformClick on each real factory-created menu item on an STA thread.
    // Target effect: The four callbacks occur once each in the approved command order.
    [Fact]
    public void Clicking_each_menu_item_emits_its_matching_command_once()
    {
        RunOnSta(() =>
        {
            var commands = new List<TrayIconCommand>();
            using var menu = TrayContextMenuFactory.Create(commands.Add);
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>()) item.PerformClick();
            Assert.Equal(
                [TrayIconCommand.ShowPet, TrayIconCommand.HidePet, TrayIconCommand.OpenStatistics, TrayIconCommand.OpenAiSettings, TrayIconCommand.Exit],
                commands);
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
