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

        // Backward compat: a legacy settings.json that only wrote
        // CompactMode=true (without DisplayMode) deserializes into
        // CompactMode=true + DisplayMode=Normal. Upgrade it to Compact.
        if (DisplayMode == DisplayMode.Normal && CompactMode)
        {
            DisplayMode = DisplayMode.Compact;
        }

        // Keep the legacy bool in sync so an older build reading this file
        // back still picks up compact mode at least.
        CompactMode = DisplayMode == DisplayMode.Compact;
    }

    public AppSettings Clone()
        => new()
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            AutoStartEnabled = AutoStartEnabled,
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
