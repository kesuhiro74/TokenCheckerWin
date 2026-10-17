using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Pure arithmetic behind the Copilot card's daily bar chart: one slot per day
// of the current UTC month, the Y scale, and the hover text. No window is
// created — this is the part the renderer must not re-derive.
public class CopilotDailySeriesTests
{
    private static readonly DateOnly Oct17 = new(2026, 10, 17);

    private static DailyCredits Day(int day, decimal credits)
        => new(new DateOnly(2026, 10, day), credits);

    [Fact]
    public void Build_FillsEveryDayOfTheMonth_AndMarksTodayAndTheFuture()
    {
        var bars = CopilotDailySeries.Build(new[] { Day(7, 1826m), Day(17, 420m) }, Oct17);

        Assert.Equal(31, bars.Count);                       // October
        Assert.Equal(new DateOnly(2026, 10, 1), bars[0].UtcDate);
        Assert.Equal(1826m, bars[6].Credits);               // the 7th
        Assert.False(bars[6].IsToday);
        Assert.False(bars[6].IsFuture);

        var today = bars[16];                               // the 17th
        Assert.True(today.IsToday);
        Assert.False(today.IsFuture);
        Assert.Equal(420m, today.Credits);

        Assert.True(bars[17].IsFuture);                     // the 18th onwards
        Assert.Equal(0m, bars[17].Credits);
        Assert.True(bars[30].IsFuture);
    }

    [Fact]
    public void Build_GapsAreZero()
    {
        var bars = CopilotDailySeries.Build(new[] { Day(7, 10m) }, Oct17);

        Assert.Equal(0m, bars[5].Credits);                  // the 6th: no usage row
        Assert.Equal(0m, bars[7].Credits);                  // the 8th: no usage row
    }

    [Fact]
    public void Build_DatesOutsideTheMonth_AreIgnored()
    {
        var bars = CopilotDailySeries.Build(
            new[]
            {
                new DailyCredits(new DateOnly(2026, 9, 30), 999m),
                new DailyCredits(new DateOnly(2026, 11, 1), 999m),
                new DailyCredits(new DateOnly(2025, 10, 7), 999m),
                Day(7, 10m)
            },
            Oct17);

        Assert.Equal(10m, bars.Sum(bar => bar.Credits));
    }

    [Fact]
    public void Build_DuplicateDates_AreSummed()
    {
        var bars = CopilotDailySeries.Build(new[] { Day(7, 10m), Day(7, 2.5m) }, Oct17);

        Assert.Equal(12.5m, bars[6].Credits);
    }

    [Fact]
    public void Build_NoData_IsAFullMonthOfZeros()
    {
        var bars = CopilotDailySeries.Build(null, new DateOnly(2026, 2, 10));

        Assert.Equal(28, bars.Count);                       // 2026 is not a leap year
        Assert.All(bars, bar => Assert.Equal(0m, bar.Credits));
        Assert.True(bars[9].IsToday);
    }

    [Fact]
    public void Build_LeapFebruary_Has29Slots()
        => Assert.Equal(29, CopilotDailySeries.Build(null, new DateOnly(2028, 2, 1)).Count);

    [Fact]
    public void Scale_TallestBarPlusHeadroom()
    {
        var bars = CopilotDailySeries.Build(new[] { Day(7, 100m), Day(8, 50m) }, Oct17);

        Assert.Equal(110m, CopilotDailySeries.Scale(bars, null));
    }

    [Fact]
    public void Scale_BudgetWins_WhenItIsHigher()
    {
        var bars = CopilotDailySeries.Build(new[] { Day(7, 100m) }, Oct17);

        Assert.Equal(220m, CopilotDailySeries.Scale(bars, 200m));
        // A lower budget does not shrink the scale below the tallest bar.
        Assert.Equal(110m, CopilotDailySeries.Scale(bars, 20m));
    }

    [Fact]
    public void Scale_AllZero_IsZero()
    {
        var bars = CopilotDailySeries.Build(null, Oct17);

        Assert.Equal(0m, CopilotDailySeries.Scale(bars, null));
        Assert.Equal(0m, CopilotDailySeries.Scale(bars, 0m));
        Assert.Equal(0m, CopilotDailySeries.Scale(Array.Empty<CopilotDailyBar>(), null));
        Assert.Equal(0m, CopilotDailySeries.Scale(null, null));
    }

    [Fact]
    public void Scale_BudgetOnly_StillScales()
        => Assert.Equal(110m, CopilotDailySeries.Scale(CopilotDailySeries.Build(null, Oct17), 100m));

    [Fact]
    public void TooltipText_IsDateAndWholeCredits()
    {
        var bar = new CopilotDailyBar(new DateOnly(2026, 10, 7), 1825.6m, IsToday: false, IsFuture: false);

        Assert.Equal("10/7  1,826 credits", CopilotDailySeries.TooltipText(bar));
    }

    [Fact]
    public void TooltipText_ZeroDay_ReadsZero()
        => Assert.Equal(
            "10/8  0 credits",
            CopilotDailySeries.TooltipText(
                new CopilotDailyBar(new DateOnly(2026, 10, 8), 0m, IsToday: true, IsFuture: false)));

    // ----- the budget line's credit value ----------------------------------

    [Fact]
    public void DailyBudgetCredits_ScalesThePercentByTheAllowance()
        => Assert.Equal(140m, CopilotWindow.DailyBudgetCredits(2d, 7000));

    [Theory]
    [InlineData(null, 7000)]
    [InlineData(2d, null)]
    [InlineData(2d, 0)]
    [InlineData(0d, 7000)]
    [InlineData(double.NaN, 7000)]
    [InlineData(double.PositiveInfinity, 7000)]
    public void DailyBudgetCredits_WithoutAUsableBudget_IsNull(double? percent, int? allowance)
        => Assert.Null(CopilotWindow.DailyBudgetCredits(percent, allowance));
}
