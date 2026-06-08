using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the daily-burn severity bands that drive the Copilot "today's delta"
// SPARK ICON color. These 4% / 5% thresholds are a ONE-DAY consumption-pace concept
// and are deliberately separate from the monthly 80% / 95% usage escalation
// (UsageThemeTests covers the latter).
public class TodayDeltaSeverityTests
{
    [Fact]
    public void Null_IsNormal()
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(null));

    [Theory]
    [InlineData(0d)]
    [InlineData(3.9d)]
    public void BelowFour_IsNormal(double percent)
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(percent));

    [Theory]
    [InlineData(4.0d)]
    [InlineData(4.9d)]
    public void FourUpToFive_IsAlert(double percent)
        => Assert.Equal(DeltaSeverity.Alert, CopilotWindow.GetTodayDeltaSeverity(percent));

    [Theory]
    [InlineData(5.0d)]
    [InlineData(12.5d)]
    public void FiveOrMore_IsRed(double percent)
        => Assert.Equal(DeltaSeverity.Red, CopilotWindow.GetTodayDeltaSeverity(percent));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFinite_IsNormal(double percent)
        => Assert.Equal(DeltaSeverity.Normal, CopilotWindow.GetTodayDeltaSeverity(percent));
}
