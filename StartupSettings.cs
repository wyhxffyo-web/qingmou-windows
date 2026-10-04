using Microsoft.Win32;

namespace QingMou;

internal static class StartupSettings
{
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
            ?? throw new InvalidOperationException("Run registry key is unavailable.");
        if (enabled) key.SetValue("QingMou", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("QingMou", false);
    }
}
