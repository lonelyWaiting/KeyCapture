using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KeyCapture.Interop;

namespace KeyCapture.Views;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _fadeTimer;
    private readonly DoubleAnimation _fadeOutAnimation;

    public OverlayWindow()
    {
        InitializeComponent();

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _fadeTimer.Tick += FadeTimer_Tick;

        _fadeOutAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        _fadeOutAnimation.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make window click-through and hidden from Alt+Tab
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    public void ShowNotification(string text)
    {
        PART_Text.Text = text;
        ShowAndReposition();
    }

    public void UpdateNotification(string text)
    {
        PART_Text.Text = text;
        ShowAndReposition();
    }

    private void ShowAndReposition()
    {
        // Stop any running fade-out and snap to visible
        PART_Container.BeginAnimation(OpacityProperty, null);
        PART_Container.Opacity = 1.0;
        Visibility = Visibility.Visible;

        // Reposition at bottom-right of work area
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 20;
        Top = workArea.Bottom - ActualHeight - 20;

        // Reset the fade timer
        _fadeTimer.Stop();
        _fadeTimer.Start();
    }

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        _fadeTimer.Stop();
        PART_Container.BeginAnimation(OpacityProperty, _fadeOutAnimation);
    }
}
