using Microsoft.Win32;

namespace TokenChecker.App;

// Detects the Windows app color mode (light/dark) for the "System" theme option.
// Read at STARTUP only — we deliberately do not subscribe to live changes; a switch
// takes effect on the next launch (matches the chosen "apply at startup" behavior).
internal static class WindowsTheme
{
    // True when Windows apps are set to dark mode. Defaults to light (false) when the
    // registry value is missing (a fresh install that never toggled the setting) or
    // unreadable.
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    // Resolves the effective dark/light from the user's theme setting: System follows
    // Windows; Light/Dark force the mode.
    public static bool ResolveDark(ThemeMode theme) => theme switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => IsDark()
    };
}
