using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Plan-label mapping for both services. Unknown values must map to null so the
// UI omits the plan instead of showing a raw API token.
public sealed class PlanLabelTests
{
    [Theory]
    // The rate-limit tier carries the Max multiplier and wins when recognized.
    [InlineData("max", "default_claude_max_5x", "Max 5x")]
    [InlineData("max", "default_claude_max_20x", "Max 20x")]
    [InlineData(null, "default_claude_max_20x", "Max 20x")]
    // Falls back to subscriptionType when the tier says nothing useful.
    [InlineData("max", "default", "Max")]
    [InlineData("max", null, "Max")]
    [InlineData("pro", "default_claude_pro", "Pro")]
    [InlineData("team", null, "Team")]
    [InlineData("enterprise", null, "Enterprise")]
    [InlineData("free", null, "Free")]
    [InlineData("MAX", null, "Max")]
    // Unknown / absent.
    [InlineData(null, null, null)]
    [InlineData("", "", null)]
    [InlineData("something_new", "some_tier", null)]
    public void ClaudePlanLabel_MapsTierAndSubscriptionType(string? subscriptionType, string? tier, string? expected)
        => Assert.Equal(expected, ClaudePlanLabel.From(subscriptionType, tier));

    [Theory]
    [InlineData("free", "Free")]
    [InlineData("go", "Go")]
    [InlineData("plus", "Plus")]
    [InlineData("pro", "Pro")]
    [InlineData("prolite", "Pro Lite")]
    [InlineData("team", "Team")]
    [InlineData("business", "Business")]
    [InlineData("self_serve_business_prolite", "Business")]
    [InlineData("self_serve_business_usage_based", "Business")]
    [InlineData("ent26", "Enterprise")]
    [InlineData("enterprise", "Enterprise")]
    [InlineData("enterprise_cbp_automation", "Enterprise")]
    [InlineData("enterprise_cbp_usage_based", "Enterprise")]
    [InlineData("edu", "Edu")]
    [InlineData("edu_plus", "Edu Plus")]
    [InlineData("edu_pro", "Edu Pro")]
    [InlineData("PLUS", "Plus")]
    // The enum's own "unknown" value, and anything added later, stay unlabeled.
    [InlineData("unknown", null)]
    [InlineData("brand_new_plan", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void CodexPlanLabel_MapsPlanType(string? planType, string? expected)
        => Assert.Equal(expected, CodexPlanLabel.From(planType));
}
