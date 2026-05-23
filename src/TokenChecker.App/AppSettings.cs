using System.Drawing;

namespace TokenChecker.App;

internal sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool AutoStartEnabled { get; set; }

    public string[] VisibleServices { get; set; } = ["Claude", "Codex"];

    public FormLocation? StatusFormLocation { get; set; }

    public bool IsServiceVisible(string serviceName)
        => VisibleServices.Any(service => string.Equals(service, serviceName, StringComparison.OrdinalIgnoreCase));

    public void Normalize()
    {
        if (!SettingsForm.RefreshIntervalOptions.Contains(RefreshIntervalSeconds))
        {
            RefreshIntervalSeconds = 60;
        }

        var visibleServices = VisibleServices
            .Where(service => string.Equals(service, "Claude", StringComparison.OrdinalIgnoreCase)
                || string.Equals(service, "Codex", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        VisibleServices = visibleServices.Length == 0 ? ["Claude", "Codex"] : visibleServices;
    }

    public AppSettings Clone()
        => new()
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            AutoStartEnabled = AutoStartEnabled,
            VisibleServices = VisibleServices.ToArray(),
            StatusFormLocation = StatusFormLocation
        };
}

internal readonly record struct FormLocation(int X, int Y)
{
    public Point ToPoint() => new(X, Y);

    public static FormLocation FromPoint(Point point) => new(point.X, point.Y);
}
