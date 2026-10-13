using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopPuppy.Pet;

public partial class PetSpeechWindow : Window
{
    public event EventHandler? BubbleClosed;

    public PetSpeechWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000); // WS_EX_NOACTIVATE
            HwndSource.FromHwnd(handle)?.AddHook(PreventActivation);
        };
    }

    private static IntPtr PreventActivation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0021) return IntPtr.Zero;
        handled = true;
        return new IntPtr(3); // MA_NOACTIVATE: close-button input still reaches the window.
    }

    private void CloseBubble_OnClick(object sender, RoutedEventArgs e)
    {
        BubbleClosed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
