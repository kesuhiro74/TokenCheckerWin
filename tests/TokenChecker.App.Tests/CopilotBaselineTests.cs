using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the manual "current-period" baseline math for Copilot AI credits
// (CopilotBaseline). After a mid-month plan change GitHub resets its dashboard
// counter but the billing API still returns the whole calendar month; the user
// calibrates once and we subtract the pre-period cumulative, but only while the
// baseline is for the current (UTC) calendar month. Pure -> timezone-fixed via an
// injected nowUtc.
public class CopilotBaselineTests
{
    private static readonly DateTimeOffset June = new(2026, 6, 17, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MonthKey_IsUtcYearMonth()
        => Assert.Equal("2026-06", CopilotBaseline.MonthKey(June));

    [Fact]
    public void ComputeBaselineUsed_IsRawMinusDashboard()
        => Assert.Equal(5126L, CopilotBaseline.ComputeBaselineUsed(9041, 3915));

    [Fact]
    public void ComputeBaselineUsed_ClampsToZeroWhenDashboardExceedsRaw()
        => Assert.Equal(0L, CopilotBaseline.ComputeBaselineUsed(100, 500));

    [Fact]
    public void EffectiveUsed_SubtractsBaseline_SameMonth()
        => Assert.Equal(3915L, CopilotBaseline.EffectiveUsed(9041, 5126, "2026-06", June));

    [Fact]
    public void EffectiveUsed_ReturnsRaw_WhenMonthRolledOver()
        => Assert.Equal(9041L, CopilotBaseline.EffectiveUsed(9041, 5126, "2026-06", July));

    [Fact]
    public void EffectiveUsed_ReturnsRaw_WhenNoBaseline()
        => Assert.Equal(9041L, CopilotBaseline.EffectiveUsed(9041, null, null, June));

    [Fact]
    public void EffectiveUsed_ClampsToZero_WhenRawBelowBaseline()
        => Assert.Equal(0L, CopilotBaseline.EffectiveUsed(100, 5126, "2026-06", June));
}
