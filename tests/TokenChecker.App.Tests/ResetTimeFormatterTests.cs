using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the Normal-mode inline reset formatters: FormatShortInline (no
// "あと" prefix, next to the 5h label) and FormatWeeklyResetOnly (reset datetime
// only, next to the weekly label). nowUtc is injected so the remaining-time
// breakdown is deterministic; expected clock strings are rebuilt from the same
// ToLocalTime() conversion so the assertions are timezone-independent. Verified
// in the default Japanese language (LocalizationTests covers the English map).
public class ResetTimeFormatterTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 6, 12, 3, 0, 0, TimeSpan.Zero);

    private static RateLimitWindow Window(DateTimeOffset? resetAtUtc) => new(
        Name: "five_hour",
        ResetAtUtc: resetAtUtc,
        Used: null,
        Limit: null,
        Remaining: null,
        UsedPercent: null,
        WindowDurationMins: 300);

    private static string LocalClock(RateLimitWindow window, string format)
        => window.ResetAtUtc!.Value.ToLocalTime().ToString(format);

    [Fact]
    public void FormatShortInline_HoursAndMinutes()
    {
        var window = Window(NowUtc.AddHours(3).AddMinutes(15));
        var expected = $"3時間15分（{LocalClock(window, "HH:mm")} リセット）";
        Assert.Equal(expected, ResetTimeFormatter.FormatShortInline(window, NowUtc));
    }

    [Fact]
    public void FormatShortInline_MinutesOnly()
    {
        var window = Window(NowUtc.AddMinutes(45));
        var expected = $"45分（{LocalClock(window, "HH:mm")} リセット）";
        Assert.Equal(expected, ResetTimeFormatter.FormatShortInline(window, NowUtc));
    }

    [Fact]
    public void FormatShortInline_Soon()
    {
        var window = Window(NowUtc.AddSeconds(30));
        var expected = $"まもなく（{LocalClock(window, "HH:mm")}リセット）";
        Assert.Equal(expected, ResetTimeFormatter.FormatShortInline(window, NowUtc));
    }

    [Fact]
    public void FormatShortInline_UnknownReset_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ResetTimeFormatter.FormatShortInline(Window(null), NowUtc));
        Assert.Equal(string.Empty, ResetTimeFormatter.FormatShortInline(null, NowUtc));
    }

    [Fact]
    public void FormatWeeklyResetOnly_FormatsMonthDayTime()
    {
        var window = Window(NowUtc.AddDays(6).AddHours(2));
        var expected = $"（{LocalClock(window, "M/d HH:mm")} リセット）";
        Assert.Equal(expected, ResetTimeFormatter.FormatWeeklyResetOnly(window));
    }

    [Fact]
    public void FormatWeeklyResetOnly_Unknown_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ResetTimeFormatter.FormatWeeklyResetOnly(Window(null)));
        Assert.Equal(string.Empty, ResetTimeFormatter.FormatWeeklyResetOnly(null));
    }
}
