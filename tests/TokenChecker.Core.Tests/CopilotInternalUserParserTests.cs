using System.Text.Json.Nodes;
using TokenChecker.Core.Providers.GitHubCopilot;
using Xunit;

namespace TokenChecker.Core.Tests;

// Fixes the auto-detection of the Copilot plan name + monthly AI-credit
// allowance from the UNOFFICIAL /copilot_internal/user response: only the
// allowlisted fields are read, an unlimited/absent/zero entitlement is NOT an
// allowance, and an unknown plan code yields no label (the App then keeps the
// title from settings).
public sealed class CopilotInternalUserParserTests
{
    // Shaped exactly like the observed real response (HTTP 200 on a Max
    // account), including the fields that must be IGNORED. The identifying ones
    // carry obvious placeholders — the point of the test is that the parser
    // never looks at them.
    private const string RealShapeJson = """
    {
      "access_type_sku": "monthly_max",
      "analytics_tracking_id": "ignore-me",
      "assigned_date": "2026-01-02T03:04:05.000Z",
      "chat_enabled": true,
      "copilot_plan": "individual_max",
      "endpoints": { "api": "https://example.invalid" },
      "enterprise_list": [1, 2],
      "id": 123456,
      "login": "ignore-me",
      "organization_list": [],
      "organization_login_list": [],
      "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
      "quota_snapshots": {
        "chat": { "entitlement": 0, "remaining": 0, "percent_remaining": 100, "unlimited": true },
        "completions": { "entitlement": 0, "remaining": 0, "percent_remaining": 100, "unlimited": true },
        "premium_interactions": {
          "entitlement": 20000,
          "remaining": 5022,
          "percent_remaining": 25.1,
          "unlimited": false
        }
      },
      "te": "ignore-me",
      "token_based_billing": true
    }
    """;

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void RealResponse_YieldsVerifiedLabelAndEntitlement()
    {
        Assert.True(CopilotInternalUserParser.TryParse(Parse(RealShapeJson), out var plan));

        Assert.Equal("Copilot Max", plan.PlanLabel);
        Assert.Equal(20000m, plan.CreditAllowance);
    }

    // Documents WHY premium_interactions is the AI-credit meter: on the observed
    // account entitlement - remaining (14,978) matched the billing API's
    // AI-credit total (14,977.95) to the credit. Asserted here from the JSON
    // itself — the parser deliberately exposes no accessor for `remaining`,
    // because nothing displays or stores it.
    [Fact]
    public void EntitlementMinusRemaining_MatchesTheBilledCreditTotal()
    {
        var snapshot = Parse(RealShapeJson)["quota_snapshots"]!["premium_interactions"]!;
        var entitlement = snapshot["entitlement"]!.GetValue<decimal>();
        var remaining = snapshot["remaining"]!.GetValue<decimal>();

        Assert.Equal(14978m, entitlement - remaining);
        Assert.Equal(14978m, Math.Round(14977.95m, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void UnknownFields_AreIgnored_AndNeverBecomeTheLabel()
    {
        CopilotInternalUserParser.TryParse(Parse(RealShapeJson), out var plan);

        // Nothing from login / id / tracking id / org lists / endpoints / te can
        // reach the label, and the raw plan code is never used as one either.
        Assert.Equal("Copilot Max", plan.PlanLabel);
        Assert.DoesNotContain("individual_max", plan.PlanLabel!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unlimited_IsNotAnAllowance()
    {
        Assert.True(CopilotInternalUserParser.TryParse(
            Parse("""
            {
              "copilot_plan": "individual_max",
              "quota_snapshots": { "premium_interactions": { "entitlement": 20000, "unlimited": true } }
            }
            """),
            out var plan));

        Assert.Equal("Copilot Max", plan.PlanLabel);
        Assert.Null(plan.CreditAllowance);
    }

    [Theory]
    // No premium_interactions snapshot at all.
    [InlineData("""{ "copilot_plan": "individual_max", "quota_snapshots": { "chat": { "entitlement": 0 } } }""")]
    // No quota_snapshots object at all.
    [InlineData("""{ "copilot_plan": "individual_max" }""")]
    // Entitlement 0 means "no credit meter", not "an allowance of zero".
    [InlineData("""{ "copilot_plan": "individual_max", "quota_snapshots": { "premium_interactions": { "entitlement": 0 } } }""")]
    // A negative entitlement is nonsense and must not become an allowance.
    [InlineData("""{ "copilot_plan": "individual_max", "quota_snapshots": { "premium_interactions": { "entitlement": -5 } } }""")]
    public void MissingOrNonPositiveEntitlement_YieldsNoAllowance(string json)
    {
        Assert.True(CopilotInternalUserParser.TryParse(Parse(json), out var plan));

        Assert.Equal("Copilot Max", plan.PlanLabel);
        Assert.Null(plan.CreditAllowance);
    }

    [Fact]
    public void UnknownPlanCode_YieldsNoLabel_ButKeepsTheAllowance()
    {
        Assert.True(CopilotInternalUserParser.TryParse(
            Parse("""
            {
              "copilot_plan": "business",
              "quota_snapshots": { "premium_interactions": { "entitlement": 300, "unlimited": false } }
            }
            """),
            out var plan));

        Assert.Null(plan.PlanLabel);
        Assert.Equal(300m, plan.CreditAllowance);
    }

    [Theory]
    [InlineData("individual_max", "Copilot Max")]
    [InlineData("INDIVIDUAL-MAX", "Copilot Max")]
    [InlineData("individual_pro_plus", "Copilot Pro+")]
    [InlineData("individual_pro", "Copilot Pro")]
    [InlineData("individual", "Copilot Pro")]
    [InlineData("free", "Copilot Free")]
    [InlineData("business", null)]
    [InlineData("enterprise", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PlanLabelMap(string? code, string? expected)
        => Assert.Equal(expected, CopilotInternalUserParser.PlanLabel(code));

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "copilot_plan": 42, "quota_snapshots": "not-an-object" }""")]
    public void NothingUsable_ReportsFalse(string json)
    {
        Assert.False(CopilotInternalUserParser.TryParse(JsonNode.Parse(json), out var plan));

        Assert.Null(plan.PlanLabel);
        Assert.Null(plan.CreditAllowance);
    }

    [Fact]
    public void NullRoot_ReportsFalse()
    {
        Assert.False(CopilotInternalUserParser.TryParse(null, out var plan));

        Assert.Null(plan.PlanLabel);
        Assert.Null(plan.CreditAllowance);
    }

    // ----- Merge ------------------------------------------------------------

    [Fact]
    public void Merge_AttachesPlanToExistingExtras()
    {
        var extras = new ServiceExtras(ModelCredits: new[] { new ModelCredits("gpt-5.6", 10m) });

        var merged = CopilotInternalUserParser.Merge(extras, new CopilotAccountPlan("Copilot Max", 20000m));

        Assert.Equal("Copilot Max", merged!.PlanLabel);
        Assert.Equal(20000m, merged.CreditAllowance);
        Assert.Single(merged.ModelCredits!);
    }

    [Fact]
    public void Merge_WithoutExtras_CreatesThem()
    {
        var merged = CopilotInternalUserParser.Merge(null, new CopilotAccountPlan("Copilot Max", 20000m));

        Assert.Equal("Copilot Max", merged!.PlanLabel);
        Assert.Equal(20000m, merged.CreditAllowance);
        Assert.Null(merged.ModelCredits);
    }

    [Fact]
    public void Merge_LabelOnly_OrAllowanceOnly_IsKept()
    {
        var labelOnly = CopilotInternalUserParser.Merge(null, new CopilotAccountPlan("Copilot Pro", null));
        Assert.Equal("Copilot Pro", labelOnly!.PlanLabel);
        Assert.Null(labelOnly.CreditAllowance);

        var allowanceOnly = CopilotInternalUserParser.Merge(null, new CopilotAccountPlan(null, 300m));
        Assert.Null(allowanceOnly!.PlanLabel);
        Assert.Equal(300m, allowanceOnly.CreditAllowance);
    }

    [Fact]
    public void Merge_NothingDetected_LeavesExtrasUntouched()
    {
        var extras = new ServiceExtras(ModelCredits: new[] { new ModelCredits("gpt-5.6", 10m) });

        Assert.Same(extras, CopilotInternalUserParser.Merge(extras, null));
        Assert.Same(extras, CopilotInternalUserParser.Merge(extras, new CopilotAccountPlan(null, null)));
        Assert.Null(CopilotInternalUserParser.Merge(null, new CopilotAccountPlan(null, null)));
    }
}
