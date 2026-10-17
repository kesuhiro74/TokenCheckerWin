using Xunit;

namespace TokenChecker.App.Tests;

// The Copilot credit row shows the EXACT monthly reset, not an estimate: the
// AI-credit allowance resets on the 1st of the month at 00:00:00 UTC (official,
// not tied to the subscription date), which the provider already computes.
//
// CopilotWindow.FormatResetShort takes the reset ALREADY in local time, so these
// assertions hold on a UTC CI runner as well as on a JST workstation.
// StringsLanguage collection: the text goes through the global Strings table.
[Collection("StringsLanguage")]
public class CopilotResetTextTests
{
    [Fact]
    public void FormatResetShort_Japanese_IsExactLocalDateAndTime()
    {
        Strings.Apply(japanese: true);

        // 2026-11-01 00:00 UTC seen from JST (UTC+9).
        Assert.Equal("リセット 11/1 9:00", CopilotWindow.FormatResetShort(new DateTime(2026, 11, 1, 9, 0, 0)));
    }

    [Fact]
    public void FormatResetShort_UsesInvariantDigitsAndNoLeadingZeros()
    {
        Strings.Apply(japanese: true);

        Assert.Equal("リセット 1/1 0:00", CopilotWindow.FormatResetShort(new DateTime(2027, 1, 1, 0, 0, 0)));
        Assert.Equal("リセット 12/1 14:30", CopilotWindow.FormatResetShort(new DateTime(2026, 12, 1, 14, 30, 0)));
    }

    [Fact]
    public void FormatResetShort_English()
    {
        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("Reset 11/1 9:00", CopilotWindow.FormatResetShort(new DateTime(2026, 11, 1, 9, 0, 0)));
            Assert.Equal("Reset at month start", CopilotWindow.FormatResetShort(null));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void FormatResetShort_NoReset_FallsBackToMonthStart()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("リセット 月初", CopilotWindow.FormatResetShort(null));
    }

    // The per-model block is the raw calendar-month total; with a plan-change
    // baseline active it is labelled as the pre-adjustment total.
    [Fact]
    public void BreakdownCaption_FlagsPreAdjustmentTotal_WhenBaselineActive()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("今月のモデル別", CopilotWindow.BreakdownCaption(false));
        Assert.Equal("今月のモデル別（補正前の累計）", CopilotWindow.BreakdownCaption(true));
    }
}
