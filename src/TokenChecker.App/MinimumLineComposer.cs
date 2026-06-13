using TokenChecker.Core;

namespace TokenChecker.App;

// The kind drives both the font (icon runs use the Nerd Font face) and the
// color resolution in the minimum-mode row painter.
internal enum MinimumRunKind
{
    ServiceIcon,
    ServiceName,
    Separator,
    SegmentIcon,
    WindowLabel,
    Percent,
    ResetIcon,
    ResetText,
    Cost
}

// One drawable run of the minimum-mode status line. PercentValue carries the
// clamped percentage for Percent runs (null = no valid value, i.e. "n/a") so
// the painter can resolve the severity color without re-parsing the text.
internal readonly record struct MinimumRun(string Text, MinimumRunKind Kind, double? PercentValue = null);

// Pure composition logic for the minimum-mode one-line status:
//   <svcIcon> Claude | <clock> 5h 38% <reset> 10:50 | <cal> 7d 39% <reset> 6/17 18:00 | <money> ¥46 (daily)
// Run texts never include inter-run whitespace — the painter inserts the gaps.
// Kept free of any drawing/WinForms state so it is directly unit-testable.
internal static class MinimumLineComposer
{
    public static IReadOnlyList<MinimumRun> Compose(
        string serviceName,
        string serviceIconGlyph,
        RateLimitWindow? fiveHour,
        RateLimitWindow? weekly,
        decimal? dailyCostJpy)
    {
        var runs = new List<MinimumRun>
        {
            new(serviceIconGlyph, MinimumRunKind.ServiceIcon),
            new(serviceName, MinimumRunKind.ServiceName),
            new("|", MinimumRunKind.Separator)
        };

        // 5-hour window: reset shown as a local wall-clock time.
        AppendWindowSegment(runs, StatusLineGlyphs.Clock, Strings.T("5h"), fiveHour, "HH:mm");
        runs.Add(new MinimumRun("|", MinimumRunKind.Separator));

        // Weekly window: reset shown as local month/day + time.
        AppendWindowSegment(runs, StatusLineGlyphs.Calendar, Strings.T("7d"), weekly, "M/d HH:mm");

        // Cost segment (icon + amount + its leading separator) is omitted as a
        // whole when today's spend is unknown.
        if (dailyCostJpy is not null)
        {
            runs.Add(new MinimumRun("|", MinimumRunKind.Separator));
            runs.Add(new MinimumRun(StatusLineGlyphs.Money, MinimumRunKind.SegmentIcon));
            runs.Add(new MinimumRun(Strings.Tf("¥{0:N0} (daily)", dailyCostJpy.Value), MinimumRunKind.Cost));
        }

        return runs;
    }

    private static void AppendWindowSegment(
        List<MinimumRun> runs,
        string iconGlyph,
        string label,
        RateLimitWindow? window,
        string resetFormat)
    {
        runs.Add(new MinimumRun(iconGlyph, MinimumRunKind.SegmentIcon));
        runs.Add(new MinimumRun(label, MinimumRunKind.WindowLabel));

        if (UsageRingRenderer.TryClampPercent(window?.UsedPercent, out var percent))
        {
            runs.Add(new MinimumRun($"{percent:0}%", MinimumRunKind.Percent, percent));
        }
        else
        {
            runs.Add(new MinimumRun("n/a", MinimumRunKind.Percent));
        }

        // Unknown reset time: drop the reset icon and the timestamp together
        // (the percent above still renders).
        if (window?.ResetAtUtc is { } resetAtUtc)
        {
            runs.Add(new MinimumRun(StatusLineGlyphs.Reset, MinimumRunKind.ResetIcon));
            runs.Add(new MinimumRun(resetAtUtc.ToLocalTime().ToString(resetFormat), MinimumRunKind.ResetText));
        }
    }
}
