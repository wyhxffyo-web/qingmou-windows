using System.Text.Json;
using System.IO;

namespace QingMou;

public sealed class AppSettings
{
    public int FocusMinutes { get; set; } = 20;
    public int BreakSeconds { get; set; } = 20;
    public string ReminderMode { get; set; } = "Partial";
    public int SnoozeMinutes { get; set; } = 10;
    public bool PauseWhenIdle { get; set; } = true;
    public bool DeferWhenFullscreen { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool LongBreakEnabled { get; set; }
    public int LongBreakEvery { get; set; } = 3;
    public int LongBreakMinutes { get; set; } = 5;
    public bool QuietHoursEnabled { get; set; }
    public string QuietStart { get; set; } = "22:00";
    public string QuietEnd { get; set; } = "08:00";
    public bool AutoStart { get; set; }
    public bool OnboardingCompleted { get; set; }
    public DateOnly StatsDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public int TodayFocusSeconds { get; set; }
    public int TodayCompletedBreaks { get; set; }

    public void Normalize()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        BreakSeconds = Math.Clamp(BreakSeconds, 5, 1800);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 60);
        LongBreakEvery = Math.Clamp(LongBreakEvery, 2, 12);
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 30);
        if (ReminderMode is not ("Partial" or "Full")) ReminderMode = "Partial";
        if (!TimeOnly.TryParseExact(QuietStart, "HH:mm", out _)) QuietStart = "22:00";
        if (!TimeOnly.TryParseExact(QuietEnd, "HH:mm", out _)) QuietEnd = "08:00";
        if (StatsDate != DateOnly.FromDateTime(DateTime.Now))
        {
            StatsDate = DateOnly.FromDateTime(DateTime.Now);
            TodayFocusSeconds = 0;
            TodayCompletedBreaks = 0;
        }
    }
}

public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QingMou");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception) { /* A damaged settings file should not stop the reminder. */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
