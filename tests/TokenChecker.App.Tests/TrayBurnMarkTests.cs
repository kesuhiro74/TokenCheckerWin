using Xunit;

namespace TokenChecker.App.Tests;

// The tray's Copilot today's-burn warning mark reuses the SAME severity bands and
// colors as the status-card spark (CopilotWindow.GetTodayDeltaSeverity /
// SeverityIconColor). Normal => no mark (null), Alert => amber, Red => red. The
// severity is now driven by the prorated daily budget (see TodayDeltaSeverityTests);
// these pin the severity -> mark-color mapping plus the end-to-end path the tray uses.
// (UsageTheme.Warning/Bad resolve through the same property at compare time, so these
// hold under either the light or dark palette.)
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

    [Fact]
    public void OverBudget_RedMark()
        => Assert.Equal(UsageTheme.Bad, TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(6.0d, 5.0d)));

    [Fact]
    public void WithinOnePointBelowBudget_AmberMark()
        => Assert.Equal(UsageTheme.Warning, TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(4.5d, 5.0d)));

    [Fact]
    public void WellUnderBudget_NoMark()
        => Assert.Null(TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(2.0d, 5.0d)));

    [Fact]
    public void NullDelta_NoMark()
        => Assert.Null(TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(null, 5.0d)));

    [Fact]
    public void NullBudget_NoMark()
        => Assert.Null(TrayIconRenderer.BurnMarkColor(CopilotWindow.GetTodayDeltaSeverity(10.0d, null)));
}
