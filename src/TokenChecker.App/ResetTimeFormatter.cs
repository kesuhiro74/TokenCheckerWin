using TokenChecker.Core;

namespace TokenChecker.App;

internal static class ResetTimeFormatter
{
    public static string FormatRemaining(RateLimitWindow? window)
    {
        if (window?.ResetAtUtc is null)
        {
            return "リセット時刻不明";
        }

        var remaining = window.ResetAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return "まもなくリセット";
        }

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return $"あと{days}日{hours}時間";
        }

        if (hours > 0)
        {
            return $"あと{hours}時間{minutes:00}分";
        }

        return $"あと{minutes}分";
    }

    public static string Format(RateLimitWindow? window)
    {
        if (window?.ResetAtUtc is null)
        {
            return "リセット時刻不明";
        }

        var localReset = window.ResetAtUtc.Value.ToLocalTime();
        var remaining = window.ResetAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return $"まもなく（{localReset:HH:mm}リセット）";
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
            return $"あと{minutes}分（{localReset:HH:mm}リセット）";
        }

        return $"あと{hours}時間{minutes:00}分（{localReset:HH:mm}リセット）";
    }

    private static string FormatWeekly(TimeSpan remaining, DateTimeOffset localReset)
    {
        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return $"あと{days}日{hours}時間（{localReset:M/d HH:mm}リセット）";
        }

        if (hours > 0)
        {
            return $"あと{hours}時間{minutes:00}分（{localReset:HH:mm}リセット）";
        }

        return $"あと{minutes}分（{localReset:HH:mm}リセット）";
    }
}
