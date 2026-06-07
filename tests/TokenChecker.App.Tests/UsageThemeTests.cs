using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the 80% / 95% severity escalation — the single source of truth that
// CLAUDE.md requires to stay identical everywhere.
public class UsageThemeTests
{
    [Fact]
    public void Thresholds_Are80And95()
    {
        Assert.Equal(80d, UsageTheme.WarningPercent);
        Assert.Equal(95d, UsageTheme.CriticalPercent);
    }

    [Fact]
    public void AccentColor_BelowWarning_IsGood()
    {
        Assert.Equal(UsageTheme.Good, UsageTheme.AccentColor(0d));
        Assert.Equal(UsageTheme.Good, UsageTheme.AccentColor(79.9d));
    }

    [Fact]
    public void AccentColor_WarningBand_IsWarning()
    {
        Assert.Equal(UsageTheme.Warning, UsageTheme.AccentColor(80d));
        Assert.Equal(UsageTheme.Warning, UsageTheme.AccentColor(94.9d));
    }

    [Fact]
    public void AccentColor_CriticalBand_IsBad()
    {
        Assert.Equal(UsageTheme.Bad, UsageTheme.AccentColor(95d));
        Assert.Equal(UsageTheme.Bad, UsageTheme.AccentColor(100d));
        Assert.Equal(UsageTheme.Bad, UsageTheme.AccentColor(150d)); // clamped to 100 -> Bad
    }

    [Fact]
    public void AccentColor_NoValue_IsMuted()
    {
        Assert.Equal(UsageTheme.MutedText, UsageTheme.AccentColor(null));
        Assert.Equal(UsageTheme.MutedText, UsageTheme.AccentColor(double.NaN));
        Assert.Equal(UsageTheme.MutedText, UsageTheme.AccentColor(double.PositiveInfinity));
    }

    [Fact]
    public void AccentColor_WithBaseColor_UsesBaseBelow80_StillEscalates()
    {
        var baseColor = System.Drawing.Color.FromArgb(10, 20, 30);
        Assert.Equal(baseColor, UsageTheme.AccentColor(50d, baseColor));
        Assert.Equal(UsageTheme.Warning, UsageTheme.AccentColor(80d, baseColor));
        Assert.Equal(UsageTheme.Bad, UsageTheme.AccentColor(95d, baseColor));
    }
}
