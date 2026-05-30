using System.Drawing;

namespace TokenChecker.App;

internal enum DisplayMode
{
    Normal = 0,
    Compact = 1,
    Minimum = 2
}

internal sealed class AppSettings
{
    public static readonly int[] AllowedRefreshIntervalSeconds = [30, 60, 300, 600];

    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool AutoStartEnabled { get; set; }

    // When true, the status window is opened automatically on launch. Defaults
    // to true so first-time users see the popup without hunting for the tray
    // icon; can be turned off in settings to start silently in the tray.
    public bool ShowOnStartup { get; set; } = true;

    // Kept for backward compatibility with settings.json written by older builds
    // that only knew about a compact-mode boolean. Normalize() reconciles this
    // with the newer DisplayMode field.
    public bool CompactMode { get; set; }

    public DisplayMode DisplayMode { get; set; } = DisplayMode.Normal;

    public string[] VisibleServices { get; set; } = ["Claude", "Codex"];

    public FormLocation? StatusFormLocation { get; set; }

    public bool IsServiceVisible(string serviceName)
        => VisibleServices?.Any(service => string.Equals(service, serviceName, StringComparison.OrdinalIgnoreCase)) == true;

    public void Normalize()
    {
        if (!AllowedRefreshIntervalSeconds.Contains(RefreshIntervalSeconds))
        {
            RefreshIntervalSeconds = 60;
        }

        VisibleServices ??= [];

        VisibleServices = VisibleServices
            .Where(service => string.Equals(service, "Claude", StringComparison.OrdinalIgnoreCase)
                || string.Equals(service, "Codex", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!Enum.IsDefined(DisplayMode))
        {
            DisplayMode = DisplayMode.Normal;
        }

        // CompactMode is a derived back-compat write only — DisplayMode is the
        // source of truth. The legacy "CompactMode=true → DisplayMode=Compact"
        // migration runs once in SettingsStore.Load (where we can tell whether
        // the JSON file actually contained a DisplayMode field). We must NOT
        // re-apply that migration here, otherwise switching from Compact back
        // to Normal in the settings dialog would immediately get reverted to
        // Compact because the clone still has CompactMode=true.
        CompactMode = DisplayMode == DisplayMode.Compact;
    }

    public AppSettings Clone()
        => new()
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            AutoStartEnabled = AutoStartEnabled,
            ShowOnStartup = ShowOnStartup,
            CompactMode = CompactMode,
            DisplayMode = DisplayMode,
            VisibleServices = VisibleServices.ToArray(),
            StatusFormLocation = StatusFormLocation
        };
}

internal readonly record struct FormLocation(int X, int Y)
{
    public Point ToPoint() => new(X, Y);

    public static FormLocation FromPoint(Point point) => new(point.X, point.Y);
}
