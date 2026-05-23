using Microsoft.Win32;

namespace TokenChecker.App;

internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TokenCheckerWin";

    public static void Apply(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (runKey is null)
            {
                return;
            }

            if (enabled)
            {
                runKey.SetValue(ValueName, Quote(Application.ExecutablePath), RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup registration is best effort; keep the app running.
        }
    }

    private static string Quote(string value)
        => $"\"{value}\"";
}
