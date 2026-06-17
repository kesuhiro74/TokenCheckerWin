using Xunit;

namespace TokenChecker.App.Tests;

// Locks down settings semantics: Copilot allowance (incl. Free=200), plan
// titles, visibility/provider gates, normalization and clone independence.
public class AppSettingsTests
{
    // The plan is passed as its int value because xUnit test methods must be
    // public, and a public method cannot take the internal CopilotPlan type.
    [Theory]
    [InlineData((int)CopilotPlan.Free, 200)]
    [InlineData((int)CopilotPlan.Pro, 1500)]
    [InlineData((int)CopilotPlan.ProPlus, 7000)]
    [InlineData((int)CopilotPlan.Max, 20000)]
    public void Allowance_FixedPlans(int plan, int expected)
        => Assert.Equal(expected, new AppSettings { CopilotPlan = (CopilotPlan)plan }.CopilotCreditAllowance());

    [Fact]
    public void Allowance_CustomPositive_UsesCustom()
        => Assert.Equal(5000, new AppSettings { CopilotPlan = CopilotPlan.Custom, CopilotCustomCredits = 5000 }.CopilotCreditAllowance());

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Allowance_CustomNonPositive_Null(int custom)
        => Assert.Null(new AppSettings { CopilotPlan = CopilotPlan.Custom, CopilotCustomCredits = custom }.CopilotCreditAllowance());

    [Fact]
    public void Allowance_None_Null()
        => Assert.Null(new AppSettings { CopilotPlan = CopilotPlan.None }.CopilotCreditAllowance());

    [Fact]
    public void Allowance_UndefinedEnum_Null()
        => Assert.Null(new AppSettings { CopilotPlan = (CopilotPlan)999 }.CopilotCreditAllowance());

    [Theory]
    [InlineData((int)CopilotPlan.Free, "Copilot Free")]
    [InlineData((int)CopilotPlan.Pro, "Copilot Pro")]
    [InlineData((int)CopilotPlan.ProPlus, "Copilot Pro+")]
    [InlineData((int)CopilotPlan.Max, "Copilot Max")]
    [InlineData((int)CopilotPlan.None, "GitHub Copilot")]
    public void PlanTitle_Fixed(int plan, string expected)
        => Assert.Equal(expected, new AppSettings { CopilotPlan = (CopilotPlan)plan }.CopilotPlanTitle());

    [Fact]
    public void PlanTitle_CustomPositive_FormatsCredits()
        => Assert.Equal($"Custom {5000:N0} credits",
            new AppSettings { CopilotPlan = CopilotPlan.Custom, CopilotCustomCredits = 5000 }.CopilotPlanTitle());

    [Fact]
    public void PlanTitle_CustomZero_Custom()
        => Assert.Equal("Custom", new AppSettings { CopilotPlan = CopilotPlan.Custom, CopilotCustomCredits = 0 }.CopilotPlanTitle());

    [Theory]
    [InlineData("Claude", true)]
    [InlineData("claude", true)]
    [InlineData("CODEX", true)]
    [InlineData("GitHub Copilot", false)]
    public void IsServiceVisible_DefaultIsClaudeCodex(string name, bool expected)
        => Assert.Equal(expected, new AppSettings().IsServiceVisible(name));

    [Fact]
    public void IsServiceVisible_EmptyOrNull_False()
    {
        Assert.False(new AppSettings { VisibleServices = [] }.IsServiceVisible("Claude"));
        Assert.False(new AppSettings { VisibleServices = null! }.IsServiceVisible("Claude"));
    }

    [Fact]
    public void ProviderGates_WindowOn_FollowVisibility()
    {
        var s = new AppSettings { ClaudeCodexWindowEnabled = true, VisibleServices = ["Claude"] };
        Assert.True(s.ClaudeProviderEnabled);
        Assert.False(s.CodexProviderEnabled);
    }

    [Fact]
    public void ProviderGates_WindowOff_AllFalse()
    {
        var s = new AppSettings { ClaudeCodexWindowEnabled = false, VisibleServices = ["Claude", "Codex"] };
        Assert.False(s.ClaudeProviderEnabled);
        Assert.False(s.CodexProviderEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopilotProviderGate_FollowsWindowEnabled(bool enabled)
        => Assert.Equal(enabled, new AppSettings { CopilotWindowEnabled = enabled }.CopilotProviderEnabled);

    [Theory]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(45, 60)]
    [InlineData(-1, 60)]
    public void Normalize_ClampsRefreshInterval(int input, int expected)
    {
        var s = new AppSettings { RefreshIntervalSeconds = input };
        s.Normalize();
        Assert.Equal(expected, s.RefreshIntervalSeconds);
    }

    [Fact]
    public void Normalize_FiltersVisibleServices_ToClaudeCodexOnly()
    {
        var s = new AppSettings { VisibleServices = ["Claude", "Codex", "GitHub Copilot"] };
        s.Normalize();
        Assert.Equal(new[] { "Claude", "Codex" }, s.VisibleServices);
    }

    [Fact]
    public void Normalize_DedupesVisibleServices_CaseInsensitive()
    {
        var s = new AppSettings { VisibleServices = ["claude", "CODEX", "Claude"] };
        s.Normalize();
        Assert.Equal(new[] { "claude", "CODEX" }, s.VisibleServices);
    }

    [Fact]
    public void Normalize_NullVisibleServices_BecomesEmpty()
    {
        var s = new AppSettings { VisibleServices = null! };
        s.Normalize();
        Assert.Empty(s.VisibleServices);
    }

    [Fact]
    public void Normalize_UndefinedEnums_ResetToDefaults()
    {
        var s = new AppSettings
        {
            DisplayMode = (DisplayMode)99,
            CopilotPlan = (CopilotPlan)99,
            CopilotAccent = (CopilotAccent)99,
            Theme = (ThemeMode)99,
            ClaudeCodexDisplayMode = (WindowDisplayMode)99,
            CopilotDisplayMode = (WindowDisplayMode)99,
        };
        s.Normalize();
        Assert.Equal(DisplayMode.Normal, s.DisplayMode);
        Assert.Equal(CopilotPlan.None, s.CopilotPlan);
        Assert.Equal(CopilotAccent.Blue, s.CopilotAccent);
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(WindowDisplayMode.Always, s.ClaudeCodexDisplayMode);
        Assert.Equal(WindowDisplayMode.Always, s.CopilotDisplayMode);
    }

    [Fact]
    public void Normalize_ClampsCustomCredits_AndMirrorsCompactMode()
    {
        var compact = new AppSettings { CopilotCustomCredits = -100, DisplayMode = DisplayMode.Compact };
        compact.Normalize();
        Assert.Equal(0, compact.CopilotCustomCredits);
        Assert.True(compact.CompactMode);

        var normal = new AppSettings { DisplayMode = DisplayMode.Normal };
        normal.Normalize();
        Assert.False(normal.CompactMode);
    }

    [Fact]
    public void Normalize_NegativeCopilotBaseline_DropsBaselineAndMonth()
    {
        var s = new AppSettings { CopilotPeriodBaselineUsed = -5, CopilotPeriodBaselineMonth = "2026-06" };
        s.Normalize();
        Assert.Null(s.CopilotPeriodBaselineUsed);
        Assert.Null(s.CopilotPeriodBaselineMonth);
    }

    [Fact]
    public void Normalize_BaselineMonthWithoutValue_DropsMonth()
    {
        var s = new AppSettings { CopilotPeriodBaselineUsed = null, CopilotPeriodBaselineMonth = "2026-06" };
        s.Normalize();
        Assert.Null(s.CopilotPeriodBaselineMonth);
    }

    [Fact]
    public void Normalize_ValidCopilotBaseline_Preserved()
    {
        var s = new AppSettings { CopilotPeriodBaselineUsed = 5126, CopilotPeriodBaselineMonth = "2026-06" };
        s.Normalize();
        Assert.Equal(5126L, s.CopilotPeriodBaselineUsed);
        Assert.Equal("2026-06", s.CopilotPeriodBaselineMonth);
    }

    [Fact]
    public void Clone_CopiesCopilotBaseline()
    {
        var s = new AppSettings { CopilotPeriodBaselineUsed = 5126, CopilotPeriodBaselineMonth = "2026-06" };
        var clone = s.Clone();
        Assert.Equal(5126L, clone.CopilotPeriodBaselineUsed);
        Assert.Equal("2026-06", clone.CopilotPeriodBaselineMonth);
    }

    [Fact]
    public void Clone_CopiesValues_AndArrayIsIndependent()
    {
        var s = new AppSettings
        {
            RefreshIntervalSeconds = 300,
            CopilotPlan = CopilotPlan.Free,
            VisibleServices = ["Claude"],
            CopilotWindowEnabled = true,
        };

        var clone = s.Clone();

        Assert.Equal(300, clone.RefreshIntervalSeconds);
        Assert.Equal(CopilotPlan.Free, clone.CopilotPlan);
        Assert.True(clone.CopilotWindowEnabled);
        Assert.Equal(s.VisibleServices, clone.VisibleServices);
        Assert.NotSame(s.VisibleServices, clone.VisibleServices);
        clone.VisibleServices[0] = "Codex";
        Assert.Equal("Claude", s.VisibleServices[0]);
    }
}
