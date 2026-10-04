using System.Windows;

namespace QingMou;

public partial class WelcomeWindow : Window
{
    private readonly AppSettings _settings;

    public WelcomeWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
    }

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        _settings.PauseWhenIdle = true;
        _settings.DeferWhenFullscreen = true;
        _settings.PlaySound = true;
        try
        {
            StartupSettings.SetEnabled(true);
            _settings.AutoStart = true;
        }
        catch (Exception)
        {
            _settings.AutoStart = false;
            System.Windows.MessageBox.Show(this, "开机启动未能开启，其余功能仍可正常使用。", "轻眸",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        _settings.OnboardingCompleted = true;
        DialogResult = true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        _settings.OnboardingCompleted = true;
        DialogResult = false;
    }
}
