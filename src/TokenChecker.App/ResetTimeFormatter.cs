using TokenChecker.Core;

namespace TokenChecker.App;

internal static class ResetTimeFormatter
{
    public static string FormatRemaining(RateLimitWindow? window)
    {
        if (window?.ResetAtUtc is null)
        {
            return Strings.T("リセット時刻不明");
        }

        var remaining = window.ResetAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return Strings.T("まもなくリセット");
        }

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return Strings.Tf("あと{0}日{1}時間", days, hours);
        }

        if (hours > 0)
        {
            return Strings.Tf("あと{0}時間{1:00}分", hours, minutes);
        }

        return Strings.Tf("あと{0}分", minutes);
    }

    public static string Format(RateLimitWindow? window)
    {
        if (window?.ResetAtUtc is null)
        {
            return Strings.T("リセット時刻不明");
        }

        var localReset = window.ResetAtUtc.Value.ToLocalTime();
        var remaining = window.ResetAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return Strings.Tf("まもなく（{0}リセット）", localReset.ToString("HH:mm"));
        }

        return window.WindowDurationMins switch
        {
            10080 => FormatWeekly(remaining, localReset),
            _ => FormatShortWindow(remaining, localReset)
        };
    }

    private static string FormatShortWindow(TimeSpan remaining, DateTimeOffset localReset)
    {
        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours <= 0)
        {
            return Strings.Tf("あと{0}分（{1}リセット）", minutes, localReset.ToString("HH:mm"));
        }

        return Strings.Tf("あと{0}時間{1:00}分（{2}リセット）", hours, minutes, localReset.ToString("HH:mm"));
    }

    private static string FormatWeekly(TimeSpan remaining, DateTimeOffset localReset)
    {
        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return Strings.Tf("あと{0}日{1}時間（{2}リセット）", days, hours, localReset.ToString("M/d HH:mm"));
        }

        if (hours > 0)
        {
            return Strings.Tf("あと{0}時間{1:00}分（{2}リセット）", hours, minutes, localReset.ToString("HH:mm"));
        }

        return Strings.Tf("あと{0}分（{1}リセット）", minutes, localReset.ToString("HH:mm"));
    }
}
