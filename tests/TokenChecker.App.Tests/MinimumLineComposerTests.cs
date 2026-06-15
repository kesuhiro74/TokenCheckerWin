using System.Globalization;
using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the minimum-mode status-line composition (MinimumLineComposer):
// run order, verbatim service name, n/a rendering, reset/cost omission rules
// and the clamped PercentValue used for severity coloring. The reset run folds
// a remaining-time token ("4h39m"/"2d") before the local reset clock; expected
// stamps are rebuilt from the same ToLocalTime() conversion (timezone-independent)
// and the remaining token is driven by an injected NowUtc (clock-independent).
// The "5h"/"7d" labels and the cost template are language-neutral (identity
// entries in the English map), so the tests do not depend on the active Strings
// language either.
[Collection("StringsLanguage")]
public class MinimumLineComposerTests
{
    // Fixed clock so the remaining-time token folded into the reset run is
    // deterministic. Resets are pinned relative to it: 5h -> exactly 4h39m away,
    // weekly -> 2d3h away (whole-day token "2d").
    private static readonly DateTimeOffset NowUtc = new(2026, 6, 15, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FiveHourReset = new(2026, 6, 15, 10, 39, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WeeklyReset = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static RateLimitWindow FiveHour(double? usedPercent, DateTimeOffset? resetAtUtc = null) => new(
        Name: "five_hour",
        ResetAtUtc: resetAtUtc,
        Used: null,
        Limit: null,
        Remaining: null,
        UsedPercent: usedPercent,
        WindowDurationMins: 300);

    private static RateLimitWindow Weekly(double? usedPercent, DateTimeOffset? resetAtUtc = null) => new(
        Name: "seven_day",
        ResetAtUtc: resetAtUtc,
        Used: null,
        Limit: null,
        Remaining: null,
        UsedPercent: usedPercent,
        WindowDurationMins: 10080);

    private static string LocalStamp(DateTimeOffset utc, string format)
        => utc.ToLocalTime().ToString(format);

    private static void AssertRuns(IReadOnlyList<MinimumRun> actual, params (MinimumRunKind Kind, string Text)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Kind, actual[i].Kind);
            Assert.Equal(expected[i].Text, actual[i].Text);
        }
    }

    [Fact]
    public void Compose_FullData_EmitsExpectedRunSequence()
    {
        Strings.Apply(japanese: true);
        try
        {
            var runs = MinimumLineComposer.Compose(
                "Claude", StatusLineGlyphs.Claude,
                FiveHour(38.0, FiveHourReset),
                Weekly(39.0, WeeklyReset),
                new DailyCost(0.29m, 46m),
                NowUtc);

            AssertRuns(runs,
            (MinimumRunKind.ServiceIcon, StatusLineGlyphs.Claude),
            (MinimumRunKind.ServiceName, "Claude"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Clock),
            (MinimumRunKind.WindowLabel, "5h"),
            (MinimumRunKind.Percent, "38%"),
            (MinimumRunKind.ResetIcon, StatusLineGlyphs.Reset),
            (MinimumRunKind.ResetText, $"4h39m {LocalStamp(FiveHourReset, "HH:mm")}"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Calendar),
            (MinimumRunKind.WindowLabel, "7d"),
            (MinimumRunKind.Percent, "39%"),
            (MinimumRunKind.ResetIcon, StatusLineGlyphs.Reset),
            (MinimumRunKind.ResetText, $"2d {LocalStamp(WeeklyReset, "M/d HH:mm")}"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Money),
            (MinimumRunKind.Cost, "¥46 (daily)"));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Compose_ServiceName_IsRenderedVerbatim()
    {
        var runs = MinimumLineComposer.Compose(
            "Codex", StatusLineGlyphs.Codex, null, null, null);

        var serviceName = Assert.Single(runs, run => run.Kind == MinimumRunKind.ServiceName);
        Assert.Equal("Codex", serviceName.Text);
    }

    [Fact]
    public void Compose_WindowMissing_RendersNaAndOmitsReset()
    {
        var runs = MinimumLineComposer.Compose(
            "Claude", StatusLineGlyphs.Claude,
            fiveHour: null,
            Weekly(39.0, WeeklyReset),
            dailyCost: null,
            NowUtc);

        AssertRuns(runs,
            (MinimumRunKind.ServiceIcon, StatusLineGlyphs.Claude),
            (MinimumRunKind.ServiceName, "Claude"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Clock),
            (MinimumRunKind.WindowLabel, "5h"),
            (MinimumRunKind.Percent, "n/a"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Calendar),
            (MinimumRunKind.WindowLabel, "7d"),
            (MinimumRunKind.Percent, "39%"),
            (MinimumRunKind.ResetIcon, StatusLineGlyphs.Reset),
            (MinimumRunKind.ResetText, $"2d {LocalStamp(WeeklyReset, "M/d HH:mm")}"));

        var naRun = runs[5];
        Assert.Null(naRun.PercentValue);
    }

    [Fact]
    public void Compose_ResetUnknown_OmitsResetRunsOnly()
    {
        var runs = MinimumLineComposer.Compose(
            "Claude", StatusLineGlyphs.Claude,
            FiveHour(42.0, resetAtUtc: null),
            weekly: null,
            dailyCost: null);

        // The percent still renders; only the reset icon + stamp are dropped.
        var percent = runs.First(run => run.Kind == MinimumRunKind.Percent);
        Assert.Equal("42%", percent.Text);
        Assert.DoesNotContain(runs, run => run.Kind == MinimumRunKind.ResetIcon);
        Assert.DoesNotContain(runs, run => run.Kind == MinimumRunKind.ResetText);
    }

    [Fact]
    public void Compose_CostNull_OmitsCostSegmentAndTrailingSeparator()
    {
        var runs = MinimumLineComposer.Compose(
            "Claude", StatusLineGlyphs.Claude,
            FiveHour(38.0, FiveHourReset),
            Weekly(39.0, WeeklyReset),
            dailyCost: null,
            NowUtc);

        // Ends on the weekly reset stamp: no money icon, no cost, and no
        // dangling third separator.
        Assert.Equal(MinimumRunKind.ResetText, runs[^1].Kind);
        Assert.DoesNotContain(runs, run => run.Kind == MinimumRunKind.Cost);
        Assert.DoesNotContain(runs, run => run.Text == StatusLineGlyphs.Money);
        Assert.Equal(2, runs.Count(run => run.Kind == MinimumRunKind.Separator));
    }

    [Fact]
    public void Compose_PercentValue_Propagated()
    {
        var runs = MinimumLineComposer.Compose(
            "Claude", StatusLineGlyphs.Claude,
            FiveHour(96.0),
            weekly: null,
            dailyCost: null);

        var percent = runs.First(run => run.Kind == MinimumRunKind.Percent);
        Assert.Equal("96%", percent.Text);
        Assert.Equal(96.0, percent.PercentValue);
    }

    [Fact]
    public void Compose_CostFormatting_JapaneseUsesYenN0()
    {
        // N0 grouping/rounding depends on the current culture; pin a known one
        // (per-thread, so this cannot leak into parallel tests).
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        Strings.Apply(japanese: true);
        try
        {
            var runs = MinimumLineComposer.Compose(
                "Claude", StatusLineGlyphs.Claude, null, null, new DailyCost(8.5m, 1234.5m));

            var cost = Assert.Single(runs, run => run.Kind == MinimumRunKind.Cost);
            Assert.Equal("¥1,235 (daily)", cost.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Compose_CostFormatting_EnglishUsesDollarsN2()
    {
        Strings.Apply(japanese: false);
        try
        {
            // English mode shows USD with cents, invariant-culture grouping,
            // independent of the JPY value.
            var runs = MinimumLineComposer.Compose(
                "Claude", StatusLineGlyphs.Claude, null, null, new DailyCost(1234.5m, 190000m));

            var cost = Assert.Single(runs, run => run.Kind == MinimumRunKind.Cost);
            Assert.Equal("$1,234.50 (daily)", cost.Text);
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Compose_BothWindowsNull_StillRendersBothSegmentsWithNa()
    {
        var runs = MinimumLineComposer.Compose(
            "Codex", StatusLineGlyphs.Codex, null, null, null);

        AssertRuns(runs,
            (MinimumRunKind.ServiceIcon, StatusLineGlyphs.Codex),
            (MinimumRunKind.ServiceName, "Codex"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Clock),
            (MinimumRunKind.WindowLabel, "5h"),
            (MinimumRunKind.Percent, "n/a"),
            (MinimumRunKind.Separator, "|"),
            (MinimumRunKind.SegmentIcon, StatusLineGlyphs.Calendar),
            (MinimumRunKind.WindowLabel, "7d"),
            (MinimumRunKind.Percent, "n/a"));
    }
}
