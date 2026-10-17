using System.Globalization;
using TokenChecker.Core;

namespace TokenChecker.App;

// One bar slot of the Copilot card's daily chart: a UTC calendar day of the
// current month, the credits consumed on it, and where it sits relative to
// today (so the renderer can color the current day and leave the rest of the
// month empty).
internal sealed record CopilotDailyBar(DateOnly UtcDate, decimal Credits, bool IsToday, bool IsFuture);

// Pure shaping of ServiceExtras.DailyCredits into one bar slot per day of the
// month, plus the chart's Y scale and hover text. Kept out of the control so
// the arithmetic is unit-testable without a window.
//
// Day semantics: GitHub's billing day starts at 00:00 UTC, i.e. 09:00 JST —
// exactly the boundary the card's "本日" delta uses — so today's bar should
// roughly match the "本日 +N" hero line.
//
// Privacy: dates and numbers only (see the note on ServiceExtras).
internal static class CopilotDailySeries
{
    // Headroom above the tallest element so the top bar never touches the
    // chart's ceiling and the budget line stays visible when it is the maximum.
    private const decimal ScaleHeadroom = 1.1m;

    // One slot per day of todayUtc's month, ascending. Days without a usage row
    // are 0 (GitHub omits them), entries that belong to another month are
    // ignored (a stale fallback snapshot after a month rollover), and equal
    // dates are summed defensively.
    public static IReadOnlyList<CopilotDailyBar> Build(IReadOnlyList<DailyCredits>? daily, DateOnly todayUtc)
    {
        var days = DateTime.DaysInMonth(todayUtc.Year, todayUtc.Month);
        var credits = new decimal[days + 1];

        if (daily is not null)
        {
            foreach (var entry in daily)
            {
                if (entry is null
                    || entry.UtcDate.Year != todayUtc.Year
                    || entry.UtcDate.Month != todayUtc.Month)
                {
                    continue;
                }

                credits[entry.UtcDate.Day] += entry.Credits;
            }
        }

        var bars = new List<CopilotDailyBar>(days);
        for (var day = 1; day <= days; day++)
        {
            bars.Add(new CopilotDailyBar(
                new DateOnly(todayUtc.Year, todayUtc.Month, day),
                credits[day],
                IsToday: day == todayUtc.Day,
                IsFuture: day > todayUtc.Day));
        }

        return bars;
    }

    // The value the top of the chart stands for: the tallest bar or the daily
    // budget line, whichever is higher, plus headroom. 0 when there is nothing
    // to scale (an all-zero month with no budget) — the renderer then draws the
    // flat baseline only.
    public static decimal Scale(IReadOnlyList<CopilotDailyBar>? bars, decimal? budgetCredits)
    {
        var max = 0m;
        if (bars is not null)
        {
            foreach (var bar in bars)
            {
                if (bar.Credits > max)
                {
                    max = bar.Credits;
                }
            }
        }

        if (budgetCredits is { } budget && budget > max)
        {
            max = budget;
        }

        return max <= 0m ? 0m : max * ScaleHeadroom;
    }

    // Hover text for one bar: "10/7  1,826 credits". The date is the UTC day
    // (invariant M/d, like the projection line) and the credits are rounded to
    // whole credits so they speak the same unit as the card's hero number.
    public static string TooltipText(CopilotDailyBar bar)
        => Strings.Tf(
            "{0}  {1} credits",
            bar.UtcDate.ToString("M/d", CultureInfo.InvariantCulture),
            Math.Round(bar.Credits, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture));
}
