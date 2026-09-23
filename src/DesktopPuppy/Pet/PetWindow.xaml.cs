using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
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

    private readonly PetInteractionPolicy _interactionPolicy = new(TimeSpan.FromSeconds(1));
    private readonly DispatcherTimer _singleClickTimer;
    private int _lineIndex;
    private Point _mouseDownPoint;
    private bool _pendingSingleClick;
    private bool _dragStarted;

    public PetWindow()
    {
        InitializeComponent();

        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
        };
        _singleClickTimer.Tick += SingleClickTimer_OnTick;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _singleClickTimer.Stop();
        base.OnClosing(e);
    }

    private void PetWindow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(this);
        _dragStarted = false;

        if (e.ClickCount >= 2)
        {
            _singleClickTimer.Stop();
            _pendingSingleClick = false;
            RegisterHit();
            e.Handled = true;
            return;
        }

        _pendingSingleClick = true;
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
        CaptureMouse();
    }

    private void PetWindow_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pendingSingleClick || e.LeftButton != MouseButtonState.Pressed || _dragStarted)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _mouseDownPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _singleClickTimer.Stop();
        _pendingSingleClick = false;
        _dragStarted = true;
        ReleaseMouseCapture();

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The window may close while a drag is being initiated.
        }
    }

    private void PetWindow_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ShowBubble(LocalLines[^1]);
        e.Handled = true;
    }

    private void PetWindow_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _singleClickTimer.Stop();
        _pendingSingleClick = false;
        RegisterHit();
        e.Handled = true;
    }

    private void SingleClickTimer_OnTick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        if (!_pendingSingleClick || _dragStarted)
        {
            return;
        }

        _pendingSingleClick = false;
        ReleaseMouseCapture();
        ShowBubble(LocalLines[_lineIndex++ % LocalLines.Length]);
    }

    private void RegisterHit()
    {
        ReleaseMouseCapture();
        if (_interactionPolicy.TryRegisterHit(DateTimeOffset.Now))
        {
            ShowBubble("嗷！轻点，我还在离线模式里努力。");
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

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
