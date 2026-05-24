using System.Drawing;

namespace TokenChecker.App;

internal sealed class AppSettings
{
    public static readonly int[] AllowedRefreshIntervalSeconds = [30, 60, 300, 600];

    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool AutoStartEnabled { get; set; }

    public bool CompactMode { get; set; }

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
    }

    public AppSettings Clone()
        => new()
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            AutoStartEnabled = AutoStartEnabled,
            CompactMode = CompactMode,
            VisibleServices = VisibleServices.ToArray(),
            StatusFormLocation = StatusFormLocation
        };
}

internal readonly record struct FormLocation(int X, int Y)
{
    public Point ToPoint() => new(X, Y);

    public static FormLocation FromPoint(Point point) => new(point.X, point.Y);
}
