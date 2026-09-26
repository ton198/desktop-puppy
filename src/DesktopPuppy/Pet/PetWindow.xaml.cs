using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPuppy.Pet.Interaction;

namespace DesktopPuppy.Pet;

public partial class PetWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static readonly string[] LocalLines =
    [
        "我看见你了。别装忙。",
        "点击成功，今天也被我盯上了。",
        "这一下算你赢半秒。",
        "右键是秘密通道，暂时离线。"
    ];

    private readonly DispatcherTimer _interactionTimer;
    private readonly InteractionController _interactionController;
    private int _lineIndex;
    private bool _dragStarted;

    public event EventHandler? StatisticsRequested;

    public PetWindow()
    {
        InitializeComponent();

        _interactionController = new InteractionController(
            new PetInteractionPolicy(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMilliseconds(GetDoubleClickTime()),
            dragThreshold: 8,
            OnInteractionEvent);

        _interactionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _interactionTimer.Tick += InteractionTimer_OnTick;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _interactionTimer.Stop();
        base.OnClosing(e);
    }

    private void PetWindow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        _interactionController.PointerPressed(
            new PointerPoint(e.GetPosition(this).X, e.GetPosition(this).Y),
            now,
            e.ClickCount);
        SyncInteractionTimer();

        if (e.ClickCount == 1)
        {
            CaptureMouse();
        }
    }

    private void PetWindow_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var point = e.GetPosition(this);
        _interactionController.PointerMoved(
            new PointerPoint(point.X, point.Y),
            DateTimeOffset.Now,
            e.LeftButton == MouseButtonState.Pressed);
        SyncInteractionTimer();
    }

    private void PetWindow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        var point = e.GetPosition(this);
        _interactionController.PointerReleased(
            new PointerPoint(point.X, point.Y),
            DateTimeOffset.Now);
        SyncInteractionTimer();
        if (!_dragStarted)
        {
            ReleaseMouseCapture();
        }

        _dragStarted = false;
    }

    private void PetWindow_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            return;
        }

        _interactionController.RightClicked(
            DateTimeOffset.Now,
            new PointerPoint(e.GetPosition(this).X, e.GetPosition(this).Y));
        e.Handled = true;
    }

    private void InteractionTimer_OnTick(object? sender, EventArgs e)
    {
        _interactionController.Advance(DateTimeOffset.Now);
        SyncInteractionTimer();
    }

    private void OnInteractionEvent(PetInteractionEvent interactionEvent)
    {
        switch (interactionEvent.Type)
        {
            case PetInteractionEventType.SingleClick:
                ShowBubble(LocalLines[_lineIndex++ % LocalLines.Length]);
                break;
            case PetInteractionEventType.Hit:
                ShowBubble("嗷！轻点，我还在离线模式里努力。");
                break;
            case PetInteractionEventType.RightClick:
                ShowBubble(LocalLines[^1]);
                break;
            case PetInteractionEventType.DragStarted:
                _dragStarted = true;
                _interactionTimer.Stop();
                ReleaseMouseCapture();
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The window may close while a drag is being initiated.
                }
                break;
            case PetInteractionEventType.DragCompleted:
                _dragStarted = false;
                break;
        }
    }

    private void SyncInteractionTimer()
    {
        if (_interactionController.HasPendingSingleClick)
        {
            if (!_interactionTimer.IsEnabled)
            {
                _interactionTimer.Start();
            }
        }
        else
        {
            _interactionTimer.Stop();
        }
    }

    private void ShowBubble(string text)
    {
        BubbleText.Text = text;
        BubbleBorder.Visibility = Visibility.Visible;
    }

    private void BubbleCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        BubbleBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void StatisticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatisticsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
