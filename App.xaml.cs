using System.Threading;
using System.Windows;

namespace QingMou;

public partial class App : System.Windows.Application
{
    private const string ActivationName = @"Local\QingMou-Activate-83CD4B4E";
    private Mutex? _singleInstance;
    private EventWaitHandle? _activationEvent;
    private bool _ownsMutex;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\QingMou-EyeReminder-83CD4B4E", out _ownsMutex);
        if (!_ownsMutex)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(ActivationName);
                activation.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var settings = SettingsStore.Load();
        if (!settings.OnboardingCompleted)
        {
            new WelcomeWindow(settings).ShowDialog();
            settings.OnboardingCompleted = true;
            SettingsStore.Save(settings);
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow(settings);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationName);
        _ = Task.Run(() =>
        {
            while (!_exiting)
            {
                try
                {
                    if (_activationEvent.WaitOne(500))
                        Dispatcher.BeginInvoke(() => ((MainWindow)MainWindow!).BringToFront());
                }
                catch (ObjectDisposedException) { break; }
            }
        });
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _activationEvent?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
