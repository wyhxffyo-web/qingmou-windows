using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QingMou;

public partial class BreakWindow : Window
{
    private static readonly System.Windows.Media.SolidColorBrush FullscreenVeilBrush = CreateFullscreenVeilBrush();
    private DispatcherTimer? _autoDismissTimer;

    public event EventHandler? SkipRequested;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? AutoDismissed;

    public BreakWindow(bool isLong, bool isPreview, int snoozeMinutes)
    {
        InitializeComponent();
        TitleText.Text = isPreview ? "提醒预览" : isLong ? "该休息一会儿了" : "让眼睛休息一下";
        TipText.Text = isLong ? "离开屏幕，活动一下身体" : "望向远方，放松双眼";
        SnoozeButton.Content = $"稍后 {snoozeMinutes} 分钟";
    }

    public void ShowOn(Forms.Screen screen, bool fullscreen)
    {
        if (fullscreen)
        {
            // Fullscreen reminders are almost opaque already. Use a solid window surface
            // to avoid the extra per-pixel transparent layer on every monitor.
            AllowsTransparency = false;
            Background = FullscreenVeilBrush;
            RootBorder.Background = FullscreenVeilBrush;
            Card.Width = 460;
            Card.Padding = new Thickness(34);
            var bounds = screen.Bounds;
            Show();
            NativeMethods.SetBoundsInPixels(this, bounds);
        }
        else
        {
            CountdownText.Visibility = Visibility.Collapsed;
            var scale = NativeMethods.GetScaleForScreen(screen);
            var width = (int)Math.Ceiling(370 * scale);
            var height = (int)Math.Ceiling(250 * scale);
            var area = screen.WorkingArea;
            var bounds = new System.Drawing.Rectangle(area.Right - width - 18, area.Bottom - height - 18, width, height);
            Show();
            NativeMethods.SetBoundsInPixels(this, bounds);
            StartAutoDismiss();
        }
    }

    private void StartAutoDismiss()
    {
        _autoDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _autoDismissTimer.Tick += (_, _) =>
        {
            _autoDismissTimer.Stop();
            AutoDismissed?.Invoke(this, EventArgs.Empty);
            Close();
        };
        Closed += (_, _) => _autoDismissTimer.Stop();
        _autoDismissTimer.Start();
    }

    private static System.Windows.Media.SolidColorBrush CreateFullscreenVeilBrush()
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 241, 231));
        brush.Freeze();
        return brush;
    }

    public void SetRemaining(int seconds)
    {
        seconds = Math.Max(0, seconds);
        CountdownText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void Snooze_Click(object sender, RoutedEventArgs e) => SnoozeRequested?.Invoke(this, EventArgs.Empty);
    private void Skip_Click(object sender, RoutedEventArgs e) => SkipRequested?.Invoke(this, EventArgs.Empty);
}
