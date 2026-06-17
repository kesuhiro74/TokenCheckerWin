using System.Globalization;

namespace TokenChecker.App;

// Manual "current-period" baseline for the Copilot AI-credit display.
//
// GitHub's billing API only returns the whole CALENDAR-MONTH AI-credit
// consumption, but a mid-month plan change resets GitHub's allowance counter so
// its dashboard shows only the post-change period (e.g. dashboard 3,915 while
// the month-to-date total is 9,041). To match the dashboard, the user calibrates
// once after a plan change by entering the dashboard's current value; we store
// the pre-period cumulative (the offset to subtract) tagged with the month it
// applies to, and subtract it from the raw monthly used. The baseline
// auto-expires once the calendar month rolls over (the API total resets then
// anyway), so a stale offset can never bleed into a new month.
//
// Only numbers and a month tag are involved (no tokens/paths/logins), so this
// stays within the privacy invariant. Pure (no I/O) -> directly unit-testable.
internal static class CopilotBaseline
{
    // The yyyy-MM (UTC) key the baseline is tagged with. UTC because the provider
    // queries usage by UTC year/month and computes the reset on the UTC month.
    public static string MonthKey(DateTimeOffset nowUtc)
        => nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // The pre-period cumulative to persist when the user calibrates: the raw
    // month-to-date used now minus the dashboard's current-period value. Never
    // negative (a dashboard value above the raw total would otherwise invert it).
    public static long ComputeBaselineUsed(long rawUsed, long dashboardCurrentUsed)
        => Math.Max(0L, rawUsed - dashboardCurrentUsed);

    // The current-period used to display: raw minus the stored baseline, but only
    // while the baseline is tagged for the current calendar month; once the month
    // rolls over the raw value is returned (the API has already reset). Clamped at
    // zero so a downward API correction can never show a negative.
    public static long EffectiveUsed(long rawUsed, long? baselineUsed, string? baselineMonth, DateTimeOffset nowUtc)
    {
        if (baselineUsed is not long baseline
            || !string.Equals(baselineMonth, MonthKey(nowUtc), StringComparison.Ordinal))
        {
            return rawUsed;
        }

        return Math.Max(0L, rawUsed - baseline);
    }
}
