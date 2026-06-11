namespace TokenChecker.App;

// App color theme for the windows. System follows the Windows app color mode
// (light/dark) at startup; Light/Dark force a mode. Applied at startup only (a
// change takes effect on the next launch).
internal enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}
