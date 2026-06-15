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
//   <svcIcon> Claude | <clock> 5h 38% <reset> 4h39m 10:50 | <cal> 7d 39% <reset> 2d 6/17 18:00 | <money> ¥46 (daily)
// The reset run carries the remaining-time token plus the reset clock; all other
// run texts never include inter-run whitespace — the painter inserts the gaps.
// Kept free of any drawing/WinForms state so it is directly unit-testable.
internal static class MinimumLineComposer
{
    public static IReadOnlyList<MinimumRun> Compose(
        string serviceName,
        string serviceIconGlyph,
        RateLimitWindow? fiveHour,
        RateLimitWindow? weekly,
        DailyCost? dailyCost)
        => Compose(serviceName, serviceIconGlyph, fiveHour, weekly, dailyCost, DateTimeOffset.UtcNow);

    // Test seam: nowUtc drives the remaining-time token deterministically.
    internal static IReadOnlyList<MinimumRun> Compose(
        string serviceName,
        string serviceIconGlyph,
        RateLimitWindow? fiveHour,
        RateLimitWindow? weekly,
        DailyCost? dailyCost,
        DateTimeOffset nowUtc)
    {
        var runs = new List<MinimumRun>
        {
            new(serviceIconGlyph, MinimumRunKind.ServiceIcon),
            new(serviceName, MinimumRunKind.ServiceName),
            new("|", MinimumRunKind.Separator)
        };

        // 5-hour window: remaining "4h39m" + local wall-clock reset time.
        AppendWindowSegment(runs, StatusLineGlyphs.Clock, Strings.T("5h"), fiveHour, "HH:mm", nowUtc);
        runs.Add(new MinimumRun("|", MinimumRunKind.Separator));

        // Weekly window: remaining days "2d" + local month/day + time reset.
        AppendWindowSegment(runs, StatusLineGlyphs.Calendar, Strings.T("7d"), weekly, "M/d HH:mm", nowUtc);

        // Cost segment (icon + amount + its leading separator) is omitted as a
        // whole when today's spend is unknown. The amount is ¥ or $ per language.
        if (dailyCost is not null)
        {
            runs.Add(new MinimumRun("|", MinimumRunKind.Separator));
            runs.Add(new MinimumRun(StatusLineGlyphs.Money, MinimumRunKind.SegmentIcon));
            runs.Add(new MinimumRun(DailyCostText.Format(dailyCost), MinimumRunKind.Cost));
        }

        return runs;
    }

    private static void AppendWindowSegment(
        List<MinimumRun> runs,
        string iconGlyph,
        string label,
        RateLimitWindow? window,
        string resetFormat,
        DateTimeOffset nowUtc)
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
            // Single reset run: "<remaining> <reset clock>" e.g. "4h39m 13:20"
            // (5h) or "2d 6/17 18:00" (weekly). The remaining token leads so the
            // glanceable "how long left" sits next to the reset icon.
            var remaining = ResetTimeFormatter.FormatCompactRemaining(window, nowUtc);
            var clock = resetAtUtc.ToLocalTime().ToString(resetFormat);
            runs.Add(new MinimumRun(StatusLineGlyphs.Reset, MinimumRunKind.ResetIcon));
            runs.Add(new MinimumRun($"{remaining} {clock}", MinimumRunKind.ResetText));
        }
    }
}
