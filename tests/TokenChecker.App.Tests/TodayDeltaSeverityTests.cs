using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the Copilot "today's delta" SPARK ICON color, now driven by a
// PRORATED daily budget instead of fixed 4%/5%: the budget is the remaining
// allowance % spread over the remaining weekdays (Mon-Fri) until the monthly
// reset. Over budget -> red; within 1 point below budget -> amber; otherwise
// green. This one-day pace concept is separate from the monthly 80/95% usage
// escalation. The weekday math is pure (wall-clock dates), so timezone-independent.
public class TodayDeltaSeverityTests
{
    // ----- GetTodayDeltaSeverity(delta, dailyBudget) ------------------------

    [Fact]
    public void OverBudget_IsRed()
        => Assert.Equal(DeltaSeverity.Red, CopilotWindow.GetTodayDeltaSeverity(6.0d, 5.0d));

    [Fact]
    public void AtBudget_IsAlert()
        => Assert.Equal(DeltaSeverity.Alert, CopilotWindow.GetTodayDeltaSeverity(5.0d, 5.0d));

    [Fact]
    public void OnePointBelowBudget_IsAlert()
        => Assert.Equal(DeltaSeverity.Alert, CopilotWindow.GetTodayDeltaSeverity(4.0d, 5.0d));

    [Fact]
    public void MoreThanOnePointBelowBudget_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(3.9d, 5.0d));

    [Fact]
    public void ZeroDelta_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(0.0d, 5.0d));

    [Fact]
    public void NegativeDelta_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(-1.0d, 5.0d));

    [Fact]
    public void NullDelta_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(null, 5.0d));

    [Fact]
    public void NullBudget_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(10.0d, null));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteDelta_IsNormal(double delta)
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(delta, 5.0d));

    [Fact]
    public void ZeroBudget_AnyPositiveDelta_IsRed()
        => Assert.Equal(DeltaSeverity.Red, CopilotWindow.GetTodayDeltaSeverity(0.5d, 0.0d));

    // ----- RemainingWeekdays(today, reset) ---------------------------------
    // June 2026 starts on Monday (2026-06-01) and has 22 weekdays.

    [Fact]
    public void RemainingWeekdays_WholeMonth()
        => Assert.Equal(22, CopilotWindow.RemainingWeekdays(new DateTime(2026, 6, 1), new DateTime(2026, 7, 1)));

    [Fact]
    public void RemainingWeekdays_FromMidWeekendCounts_OnlyWeekdays()
        // From Sat 2026-06-06 to month end = 22 total - 5 (Mon-Fri of week 1) = 17.
        => Assert.Equal(17, CopilotWindow.RemainingWeekdays(new DateTime(2026, 6, 6), new DateTime(2026, 7, 1)));

    [Fact]
    public void RemainingWeekdays_LastTwoWeekdays()
        => Assert.Equal(2, CopilotWindow.RemainingWeekdays(new DateTime(2026, 6, 29), new DateTime(2026, 7, 1)));

    [Fact]
    public void RemainingWeekdays_LastWeekday()
        => Assert.Equal(1, CopilotWindow.RemainingWeekdays(new DateTime(2026, 6, 30), new DateTime(2026, 7, 1)));

    [Fact]
    public void RemainingWeekdays_NoneLeft_ClampsToOne()
        => Assert.Equal(1, CopilotWindow.RemainingWeekdays(new DateTime(2026, 7, 1), new DateTime(2026, 7, 1)));

    // ----- DailyBudgetPercent(usedPercent, remainingWeekdays) --------------

    [Fact]
    public void DailyBudget_SpreadsRemainingOverWeekdays()
        => Assert.Equal(5.0d, CopilotWindow.DailyBudgetPercent(50.0d, 10));

    [Fact]
    public void DailyBudget_NullUsed_IsNull()
        => Assert.Null(CopilotWindow.DailyBudgetPercent(null, 10));

    [Fact]
    public void DailyBudget_OverHundredUsed_ClampsRemainingToZero()
        => Assert.Equal(0.0d, CopilotWindow.DailyBudgetPercent(120.0d, 5));

    [Fact]
    public void DailyBudget_ZeroWeekdays_ClampsDivisorToOne()
        => Assert.Equal(10.0d, CopilotWindow.DailyBudgetPercent(90.0d, 0));
}
