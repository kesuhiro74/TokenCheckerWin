using Xunit;

namespace TokenChecker.App.Tests;

// The tray's Copilot today's-burn warning mark reuses the SAME severity bands and
// colors as the status-card spark (CopilotWindow.GetTodayDeltaSeverity /
// SeverityIconColor). These tests pin the tray-side mapping that drives the mark:
// Normal => no mark (null), Alert (4-5%) => amber, Red (>=5%) => red — plus the
// end-to-end today's-percent -> severity -> mark-color path the tray actually uses.
// (UsageTheme.Warning/Bad are resolved through the same property at compare time, so
// these assertions hold under either the light or dark palette.)
public class TrayBurnMarkTests
{
    [Fact]
    public void NormalSeverity_HasNoMark()
        => Assert.Null(TrayIconRenderer.BurnMarkColor(DeltaSeverity.Normal));

    [Fact]
    public void AlertSeverity_IsAmber()
        => Assert.Equal(UsageTheme.Warning, TrayIconRenderer.BurnMarkColor(DeltaSeverity.Alert));

    [Fact]
    public void RedSeverity_IsRed()
        => Assert.Equal(UsageTheme.Bad, TrayIconRenderer.BurnMarkColor(DeltaSeverity.Red));

    [Theory]
    [InlineData(0d)]
    [InlineData(3.9d)]
    public void BelowFourPercent_NoMark(double todayPercent)
        => Assert.Null(TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(todayPercent)));

    [Theory]
    [InlineData(4.0d)]
    [InlineData(4.9d)]
    public void FourUpToFivePercent_Amber(double todayPercent)
        => Assert.Equal(UsageTheme.Warning, TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(todayPercent)));

    [Theory]
    [InlineData(5.0d)]
    [InlineData(20d)]
    public void FivePercentOrMore_Red(double todayPercent)
        => Assert.Equal(UsageTheme.Bad, TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(todayPercent)));

    [Fact]
    public void NullPercent_NoMark()
        => Assert.Null(TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(null)));
}
