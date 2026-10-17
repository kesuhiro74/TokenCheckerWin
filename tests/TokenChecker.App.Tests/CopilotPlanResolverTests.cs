using TokenChecker.App;
using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Fixes the precedence between the plan/allowance auto-detected from GitHub
// (ServiceExtras) and the plan picked in settings: the API wins, the settings
// plan is the fallback, and the extras come from the sample the displayed
// numbers come from (current when Available, otherwise the last successful one).
public sealed class CopilotPlanResolverTests
{
    private static ServiceUsage Copilot(ProviderStatus status, ServiceExtras? extras)
        => new("GitHub Copilot", status, null, Array.Empty<RateLimitWindow>(), extras);

    private static ServiceExtras Detected(string? label, decimal? allowance)
        => new(PlanLabel: label, CreditAllowance: allowance);

    // ----- PickExtras -------------------------------------------------------

    [Fact]
    public void PickExtras_UsesCurrent_WhenAvailable()
    {
        var current = Copilot(ProviderStatus.Available, Detected("Copilot Max", 20000m));
        var fallback = Copilot(ProviderStatus.Available, Detected("Copilot Pro", 1500m));

        Assert.Equal("Copilot Max", CopilotPlanResolver.PickExtras(current, fallback)!.PlanLabel);
    }

    [Fact]
    public void PickExtras_UsesFallback_WhenCurrentIsNotAvailable()
    {
        var current = Copilot(ProviderStatus.RateLimited, null);
        var fallback = Copilot(ProviderStatus.Available, Detected("Copilot Max", 20000m));

        Assert.Equal("Copilot Max", CopilotPlanResolver.PickExtras(current, fallback)!.PlanLabel);
    }

    [Fact]
    public void PickExtras_NoSampleAtAll_IsNull()
        => Assert.Null(CopilotPlanResolver.PickExtras(null, null));

    // ----- Allowance --------------------------------------------------------

    [Fact]
    public void Allowance_ApiWinsOverSettings()
        => Assert.Equal(20000, CopilotPlanResolver.Allowance(Detected("Copilot Max", 20000m), 1500));

    [Fact]
    public void Allowance_FallsBackToSettings_WhenApiHasNone()
    {
        Assert.Equal(1500, CopilotPlanResolver.Allowance(Detected("Copilot Max", null), 1500));
        Assert.Equal(1500, CopilotPlanResolver.Allowance(null, 1500));
    }

    [Fact]
    public void Allowance_NeitherSource_IsNull()
    {
        Assert.Null(CopilotPlanResolver.Allowance(null, null));
        Assert.Null(CopilotPlanResolver.Allowance(new ServiceExtras(PlanLabel: "Copilot Max"), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allowance_NonPositiveApiValue_FallsBackToSettings(int detected)
        => Assert.Equal(1500, CopilotPlanResolver.Allowance(Detected(null, detected), 1500));

    [Fact]
    public void Allowance_OutOfIntRangeApiValue_FallsBackToSettings()
        => Assert.Equal(1500, CopilotPlanResolver.Allowance(Detected(null, 3_000_000_000m), 1500));

    [Fact]
    public void Allowance_FractionalApiValue_IsRoundedAwayFromZero()
        => Assert.Equal(1501, CopilotPlanResolver.Allowance(Detected(null, 1500.5m), 1500));

    // ----- Title ------------------------------------------------------------

    [Fact]
    public void Title_ApiLabelWinsOverSettingsTitle()
        => Assert.Equal("Copilot Max", CopilotPlanResolver.Title(Detected("Copilot Max", 20000m), "Copilot Pro"));

    [Fact]
    public void Title_FallsBackToSettings_WhenApiHasNoLabel()
    {
        // An unknown plan code yields no label but may still yield an allowance;
        // the title then stays the one derived from settings.
        Assert.Equal("Copilot Pro", CopilotPlanResolver.Title(Detected(null, 300m), "Copilot Pro"));
        Assert.Equal("GitHub Copilot", CopilotPlanResolver.Title(null, "GitHub Copilot"));
        Assert.Equal("GitHub Copilot", CopilotPlanResolver.Title(Detected("  ", null), "GitHub Copilot"));
    }

    // ----- The two together (what the card/tray actually ask for) -----------

    [Fact]
    public void FallbackSampleSuppliesPlanAndAllowance_WhenTheRefreshFailed()
    {
        var current = Copilot(ProviderStatus.Error, null);
        var fallback = Copilot(ProviderStatus.Available, Detected("Copilot Max", 20000m));
        var extras = CopilotPlanResolver.PickExtras(current, fallback);

        Assert.Equal(20000, CopilotPlanResolver.Allowance(extras, null));
        Assert.Equal("Copilot Max", CopilotPlanResolver.Title(extras, "GitHub Copilot"));
    }

    // With an allowance in force the card's "pick a plan" hint cannot appear:
    // the hint is gated on the allowance the resolver returns (see
    // CopilotWindow.ServiceCard.Update's hasPlan), and the settings plan here is
    // None (null).
    [Fact]
    public void DetectedAllowance_MeansTheCardHasAPlan_EvenWithPlanNoneInSettings()
        => Assert.NotNull(CopilotPlanResolver.Allowance(Detected("Copilot Max", 20000m), null));
}
