using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QingMou;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush ActiveStatusBrush = CreateFrozenBrush(0x5E, 0x9C, 0x79);
    private static readonly SolidColorBrush InactiveStatusBrush = CreateFrozenBrush(0xB1, 0xBD, 0xB3);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<BreakWindow> _breakWindows = [];
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayPauseItem;
    private bool _ready;
    private bool _exiting;
    private bool _manuallyPaused;
    private bool _sessionLocked;
    private bool _breakActive;
    private bool _previewActive;
    private bool _snoozeActive;
    private int _remainingFocusSeconds;
    private int _completedFocusCycles;
    private int _previewPreviousRemaining;
    private double _fractionalFocusSeconds;
    private DateTimeOffset _lastTick = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastSaved = DateTimeOffset.UtcNow;
    private DateTimeOffset _breakEndsAt;
    private bool? _lastStatusIsActive;
    private bool? _lastPauseButtonIsPaused;
    private int _lastCycleMinutes = -1;
    private int _lastSnoozeMinutes = -1;
    private int _lastStatsFocusMinutes = -1;
    private int _lastStatsBreakCount = -1;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _remainingFocusSeconds = settings.FocusMinutes * 60;
        LoadControls();
        InitializeTray();
        SystemEvents.SessionSwitch += SessionSwitch;
        _timer.Tick += Timer_Tick;
        _timer.Start();
        _ready = true;
        UpdateDisplay();
    }

    private void LoadControls()
    {
        FocusMinutesBox.Text = _settings.FocusMinutes.ToString();
        BreakSecondsBox.Text = _settings.BreakSeconds.ToString();
        SnoozeMinutesBox.Text = _settings.SnoozeMinutes.ToString();
        LongEveryBox.Text = _settings.LongBreakEvery.ToString();
        LongMinutesBox.Text = _settings.LongBreakMinutes.ToString();
        QuietStartBox.Text = _settings.QuietStart;
        QuietEndBox.Text = _settings.QuietEnd;
        PartialModeRadio.IsChecked = _settings.ReminderMode != "Full";
        FullModeRadio.IsChecked = _settings.ReminderMode == "Full";
        FullscreenCheck.IsChecked = _settings.DeferWhenFullscreen;
        IdleCheck.IsChecked = _settings.PauseWhenIdle;
        SoundCheck.IsChecked = _settings.PlaySound;
        LongBreakCheck.IsChecked = _settings.LongBreakEnabled;
        QuietCheck.IsChecked = _settings.QuietHoursEnabled;
        AutoStartCheck.IsChecked = _settings.AutoStart;
    }

    private void InitializeTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示轻眸", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        _trayPauseItem = new Forms.ToolStripMenuItem("暂停提醒");
        _trayPauseItem.Click += (_, _) => Dispatcher.Invoke(TogglePause);
        menu.Items.Add(_trayPauseItem);
        menu.Items.Add("立即休息", null, (_, _) => Dispatcher.Invoke(() => StartBreak(false)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApp));

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information,
            Text = "轻眸 · 护眼提醒",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        ResetStatsForNewDay();

        if (_breakActive)
        {
            var left = Math.Max(0, (int)Math.Ceiling((_breakEndsAt - now).TotalSeconds));
            foreach (var window in _breakWindows) window.SetRemaining(left);
            if (left == 0) EndBreak(true);
            RefreshDisplayIfVisible();
            return;
        }

        var pauseReason = GetPauseReason();
        if (pauseReason is not null)
        {
            RefreshDisplayIfVisible(pauseReason);
            return;
        }

        // A long gap means the PC slept or this process was suspended.
        if (elapsed is > 0 and < 10)
        {
            _fractionalFocusSeconds += elapsed;
            var wholeSeconds = (int)_fractionalFocusSeconds;
            _fractionalFocusSeconds -= wholeSeconds;
            if (wholeSeconds > 0 && _remainingFocusSeconds > 0)
            {
                var counted = Math.Min(wholeSeconds, _remainingFocusSeconds);
                _remainingFocusSeconds -= counted;
                _settings.TodayFocusSeconds += counted;
            }
        }

        if (_remainingFocusSeconds <= 0)
        {
            if (_settings.DeferWhenFullscreen && NativeMethods.ForegroundIsFullscreen())
            {
                RefreshDisplayIfVisible("全屏应用中，休息提醒已暂缓");
                return;
            }
            StartBreak(false);
            return;
        }

        if ((now - _lastSaved).TotalSeconds >= 60) SaveSettings();
        RefreshDisplayIfVisible();
    }

    private string? GetPauseReason()
    {
        if (_manuallyPaused) return "提醒已暂停";
        if (_sessionLocked) return "锁屏期间暂停计时";
        if (IsQuietTime()) return "免打扰时段，计时已暂停";
        if (_settings.PauseWhenIdle && NativeMethods.IdleTime >= TimeSpan.FromMinutes(5))
            return "离开电脑，计时已暂停";
        return null;
    }

    private bool IsQuietTime()
    {
        if (!_settings.QuietHoursEnabled ||
            !TimeOnly.TryParseExact(_settings.QuietStart, "HH:mm", out var start) ||
            !TimeOnly.TryParseExact(_settings.QuietEnd, "HH:mm", out var end)) return false;
        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (start == end) return false;
        return start < end ? now >= start && now < end : now >= start || now < end;
    }

    private void StartBreak(bool preview)
    {
        if (_breakActive) return;
        _breakActive = true;
        _previewActive = preview;
        if (preview) _previewPreviousRemaining = _remainingFocusSeconds;
        else
        {
            _snoozeActive = false;
            _completedFocusCycles++;
        }
        var isLong = !preview && _settings.LongBreakEnabled &&
                     _completedFocusCycles % _settings.LongBreakEvery == 0;
        var duration = preview ? 8 : isLong ? _settings.LongBreakMinutes * 60 : _settings.BreakSeconds;
        _breakEndsAt = DateTimeOffset.UtcNow.AddSeconds(duration);

        var fullscreen = _settings.ReminderMode == "Full";
        var screens = fullscreen
            ? Forms.Screen.AllScreens
            : [Forms.Screen.FromPoint(Forms.Cursor.Position)];
        foreach (var screen in screens)
        {
            var window = new BreakWindow(isLong, preview, _settings.SnoozeMinutes);
            window.SkipRequested += (_, _) => EndBreak(false);
            window.SnoozeRequested += (_, _) => SnoozeBreak();
            window.AutoDismissed += (_, _) => _breakWindows.Remove(window);
            window.SetRemaining(duration);
            _breakWindows.Add(window);
            window.ShowOn(screen, fullscreen);
        }
        if (_settings.PlaySound) SystemSounds.Asterisk.Play();
        RefreshDisplayIfVisible();
    }

    private void EndBreak(bool completed)
    {
        if (!_breakActive) return;
        foreach (var window in _breakWindows) window.Close();
        _breakWindows.Clear();
        _breakActive = false;
        if (_previewActive)
        {
            _remainingFocusSeconds = _previewPreviousRemaining;
            _previewActive = false;
        }
        else
        {
            if (completed) _settings.TodayCompletedBreaks++;
            _remainingFocusSeconds = _settings.FocusMinutes * 60;
        }
        _lastTick = DateTimeOffset.UtcNow;
        SaveSettings();
        RefreshDisplayIfVisible();
    }

    private void SnoozeBreak()
    {
        if (!_breakActive) return;
        var preview = _previewActive;
        EndBreak(false);
        if (!preview)
        {
            _snoozeActive = true;
            _remainingFocusSeconds = _settings.SnoozeMinutes * 60;
            RefreshDisplayIfVisible();
        }
    }

    private void UpdateDisplay(string? reason = null)
    {
        var seconds = _breakActive
            ? Math.Max(0, (int)Math.Ceiling((_breakEndsAt - DateTimeOffset.UtcNow).TotalSeconds))
            : Math.Max(0, _remainingFocusSeconds);
        CountdownText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        if (_lastCycleMinutes != _settings.FocusMinutes)
        {
            CycleText.Text = _settings.FocusMinutes.ToString();
            _lastCycleMinutes = _settings.FocusMinutes;
        }
        FocusProgress.Value = _breakActive ? 100 : Math.Clamp(
            100.0 * (_settings.FocusMinutes * 60 - _remainingFocusSeconds) / (_settings.FocusMinutes * 60), 0, 100);
        var label = _breakActive ? "正在休息" : reason ?? "正在专注";
        if (StatusText.Text != label) StatusText.Text = label;

        var statusIsActive = (reason is null && !_manuallyPaused) || _breakActive;
        if (_lastStatusIsActive != statusIsActive)
        {
            StatusDot.Fill = statusIsActive ? ActiveStatusBrush : InactiveStatusBrush;
            _lastStatusIsActive = statusIsActive;
        }

        var caption = _breakActive ? "望向远方，放松双眼" : reason ?? "保持专注，也记得放松眼睛";
        if (CaptionText.Text != caption) CaptionText.Text = caption;

        if (_lastPauseButtonIsPaused != _manuallyPaused)
        {
            var pauseText = _manuallyPaused ? "继续提醒" : "暂停提醒";
            PauseButton.Content = pauseText;
            if (_trayPauseItem is not null) _trayPauseItem.Text = pauseText;
            _lastPauseButtonIsPaused = _manuallyPaused;
        }

        if (_snoozeActive)
        {
            SnoozeButton.Content = "已延后提醒";
            SnoozeButton.IsEnabled = false;
        }
        else
        {
            SnoozeButton.IsEnabled = true;
            if (_lastSnoozeMinutes != _settings.SnoozeMinutes || SnoozeButton.Content?.ToString() == "已延后提醒")
            {
                SnoozeButton.Content = $"稍后 {_settings.SnoozeMinutes} 分钟";
                _lastSnoozeMinutes = _settings.SnoozeMinutes;
            }
        }

        var focusMinutes = _settings.TodayFocusSeconds / 60;
        if (_lastStatsFocusMinutes != focusMinutes || _lastStatsBreakCount != _settings.TodayCompletedBreaks)
        {
            StatsText.Text = $"今日专注 {focusMinutes} 分钟 · 完成休息 {_settings.TodayCompletedBreaks} 次";
            _lastStatsFocusMinutes = focusMinutes;
            _lastStatsBreakCount = _settings.TodayCompletedBreaks;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void RefreshDisplayIfVisible(string? reason = null)
    {
        if (IsVisible) UpdateDisplay(reason);
    }

    private void ResetStatsForNewDay()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_settings.StatsDate == today) return;
        _settings.StatsDate = today;
        _settings.TodayFocusSeconds = 0;
        _settings.TodayCompletedBreaks = 0;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
            _lastSaved = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            StatusText.Text = "设置保存失败";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void ApplyControls()
    {
        if (!_ready) return;
        var previousFocus = _settings.FocusMinutes;
        _settings.FocusMinutes = ReadInt(FocusMinutesBox, _settings.FocusMinutes, 1, 180);
        _settings.BreakSeconds = ReadInt(BreakSecondsBox, _settings.BreakSeconds, 5, 1800);
        _settings.SnoozeMinutes = ReadInt(SnoozeMinutesBox, _settings.SnoozeMinutes, 1, 60);
        _settings.LongBreakEvery = ReadInt(LongEveryBox, _settings.LongBreakEvery, 2, 12);
        _settings.LongBreakMinutes = ReadInt(LongMinutesBox, _settings.LongBreakMinutes, 1, 30);
        _settings.QuietStart = ReadTime(QuietStartBox, _settings.QuietStart);
        _settings.QuietEnd = ReadTime(QuietEndBox, _settings.QuietEnd);
        _settings.ReminderMode = FullModeRadio.IsChecked == true ? "Full" : "Partial";
        _settings.DeferWhenFullscreen = FullscreenCheck.IsChecked == true;
        _settings.PauseWhenIdle = IdleCheck.IsChecked == true;
        _settings.PlaySound = SoundCheck.IsChecked == true;
        _settings.LongBreakEnabled = LongBreakCheck.IsChecked == true;
        _settings.QuietHoursEnabled = QuietCheck.IsChecked == true;
        var requestedAutostart = AutoStartCheck.IsChecked == true;
        if (requestedAutostart != _settings.AutoStart)
        {
            try { StartupSettings.SetEnabled(requestedAutostart); _settings.AutoStart = requestedAutostart; }
            catch (Exception)
            {
                AutoStartCheck.IsChecked = _settings.AutoStart;
                System.Windows.MessageBox.Show(this, "无法修改开机启动设置。", "轻眸", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (_settings.FocusMinutes != previousFocus)
        {
            _remainingFocusSeconds = _settings.FocusMinutes * 60;
            _snoozeActive = false;
        }
        SaveSettings();
        UpdateDisplay();
    }

    private static int ReadInt(System.Windows.Controls.TextBox box, int fallback, int min, int max)
    {
        var value = int.TryParse(box.Text, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
        box.Text = value.ToString();
        return value;
    }

    private static string ReadTime(System.Windows.Controls.TextBox box, string fallback)
    {
        if (!TimeOnly.TryParseExact(box.Text.Trim(), "HH:mm", out var parsed))
        {
            box.Text = fallback;
            return fallback;
        }
        box.Text = parsed.ToString("HH:mm");
        return box.Text;
    }

    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            _sessionLocked = e.Reason == SessionSwitchReason.SessionLock;
            _lastTick = DateTimeOffset.UtcNow;
            RefreshDisplayIfVisible();
        });
    }

    internal void BringToFront() => ShowMainWindow();

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateDisplay();
    }

    private void TogglePause()
    {
        _manuallyPaused = !_manuallyPaused;
        _lastTick = DateTimeOffset.UtcNow;
        RefreshDisplayIfVisible(_manuallyPaused ? "提醒已暂停" : null);
    }

    private void ExitApp()
    {
        _exiting = true;
        _timer.Stop();
        SystemEvents.SessionSwitch -= SessionSwitch;
        foreach (var window in _breakWindows) window.Close();
        _breakWindows.Clear();
        SaveSettings();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        Hide();
        _tray?.ShowBalloonTip(2500, "轻眸仍在运行", "可从任务栏通知区域打开或退出", Forms.ToolTipIcon.Info);
    }

    private void BreakNow_Click(object sender, RoutedEventArgs e) => StartBreak(false);
    private void Preview_Click(object sender, RoutedEventArgs e) => StartBreak(true);
    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        if (_breakActive) SnoozeBreak();
        else if (!_snoozeActive)
        {
            _snoozeActive = true;
            _remainingFocusSeconds = _settings.SnoozeMinutes * 60;
            UpdateDisplay();
        }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void SettingsField_LostFocus(object sender, RoutedEventArgs e) => ApplyControls();
    private void SettingsChoice_Changed(object sender, SelectionChangedEventArgs e) => ApplyControls();
    private void SettingsToggle_Changed(object sender, RoutedEventArgs e) => ApplyControls();
    private void SettingsField_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyControls();
        Keyboard.ClearFocus();
    }
}
