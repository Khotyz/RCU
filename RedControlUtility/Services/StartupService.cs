using Microsoft.Win32;

namespace RedControlUtility.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RedControlUtility";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, bool minimized)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (k == null) return false;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(exe)) return false;
                k.SetValue(ValueName, $"\"{exe}\"{(minimized ? " --minimized" : string.Empty)}", RegistryValueKind.String);
            }
            else
            {
                k.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"StartupService: {ex.Message}");
            return false;
        }
    }
}
