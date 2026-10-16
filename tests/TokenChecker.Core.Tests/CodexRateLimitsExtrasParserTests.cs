using System.Text.Json.Nodes;
using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the Codex app-server account/rateLimits/read extras against the
// shape observed with codex-cli 0.156.1: credits, individualLimit and the
// rate-limit reset credits ("reset tickets").
public sealed class CodexRateLimitsExtrasParserTests
{
    // Trimmed to the keys we read. Credit amounts really are strings in the
    // app-server response. The reset-credit id/title/description fields are
    // present in the wire format but deliberately not read, so they are
    // included here to prove they are ignored.
    private const string ObservedResponse = """
    {
      "rateLimits": {
        "planType": "plus",
        "credits": { "hasCredits": true, "unlimited": false, "balance": "12.5" },
        "individualLimit": null,
        "spendControlReached": false,
        "rateLimitReachedType": null
      },
      "rateLimitResetCredits": {
        "availableCount": 2,
        "credits": [
          { "id": "rc_1", "resetType": "weekly", "status": "available", "grantedAt": 1760000000,
            "expiresAt": 1761000000, "title": "Reset credit", "description": "Resets your weekly limit" },
          { "id": "rc_2", "resetType": "weekly", "status": "available", "grantedAt": 1760000000,
            "expiresAt": 1760500000, "title": "Reset credit", "description": "Resets your weekly limit" },
          { "id": "rc_3", "resetType": "weekly", "status": "consumed", "grantedAt": 1759000000,
            "expiresAt": 1759500000, "title": "Reset credit", "description": "Resets your weekly limit" }
        ]
      }
    }
    """;

    private static ServiceExtras Parse(string json, string? planTypeFromAccount = null)
        => CodexRateLimitsExtrasParser.Parse(JsonNode.Parse(json), planTypeFromAccount);

    [Fact]
    public void Parse_ObservedResponse_ReadsPlanCreditsAndResetCredits()
    {
        var extras = Parse(ObservedResponse);

        Assert.Equal("Plus", extras.PlanLabel);
        Assert.Equal(12.5m, extras.CreditsBalance);
        Assert.False(extras.CreditsUnlimited);
        Assert.Equal(2, extras.ResetCreditsAvailable);
        // Earliest expiry among the AVAILABLE credits only (rc_2), not the
        // consumed one.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760500000), extras.ResetCreditsNextExpiry);
        // Codex has no model-scoped weekly limits.
        Assert.Null(extras.ScopedLimits);
        Assert.Null(extras.ExtraSpend);
    }

    [Fact]
    public void Parse_FallsBackToTheAccountPlanType()
    {
        var extras = Parse("""{ "rateLimits": { "credits": null } }""", "pro");

        Assert.Equal("Pro", extras.PlanLabel);
    }

    [Fact]
    public void Parse_SnapshotPlanType_WinsOverTheAccountPlanType()
    {
        var extras = Parse("""{ "rateLimits": { "planType": "team" } }""", "plus");

        Assert.Equal("Team", extras.PlanLabel);
    }

    [Fact]
    public void Parse_NullResponse_StillReportsTheAccountPlan()
    {
        var extras = CodexRateLimitsExtrasParser.Parse(null, "plus");

        Assert.Equal("Plus", extras.PlanLabel);
        Assert.Null(extras.CreditsBalance);
        Assert.Null(extras.ResetCreditsAvailable);
    }

    [Fact]
    public void Parse_WithoutCredits_ReportsNoBalance()
    {
        var extras = Parse("""
        { "rateLimits": { "credits": { "hasCredits": false, "unlimited": false, "balance": null } } }
        """);

        Assert.Null(extras.CreditsBalance);
        Assert.False(extras.CreditsUnlimited);
    }

    [Fact]
    public void Parse_BalanceIsIgnored_WhenTheAccountHasNoCredits()
    {
        // A stale balance on an account without credits is noise, not data.
        var extras = Parse("""
        { "rateLimits": { "credits": { "hasCredits": false, "unlimited": false, "balance": "99" } } }
        """);

        Assert.Null(extras.CreditsBalance);
    }

    [Fact]
    public void Parse_UnlimitedCredits_AreReported()
    {
        var extras = Parse("""
        { "rateLimits": { "credits": { "hasCredits": false, "unlimited": true, "balance": null } } }
        """);

        Assert.True(extras.CreditsUnlimited);
        Assert.False(extras.IsEmpty);
    }

    [Fact]
    public void Parse_IndividualLimit_BecomesExtraSpendInCredits()
    {
        var extras = Parse("""
        {
          "rateLimits": {
            "individualLimit": { "limit": "200", "used": "37.5", "remainingPercent": 81,
                                 "resetsAt": 1761000000 }
          }
        }
        """);

        var spend = extras.ExtraSpend!;
        Assert.Equal(37.5m, spend.Used);
        Assert.Equal(200m, spend.Limit);
        Assert.Equal("credits", spend.Unit);
    }

    [Fact]
    public void Parse_IndividualLimit_AcceptsNumericAmounts()
    {
        var extras = Parse("""
        { "rateLimits": { "individualLimit": { "limit": 200, "used": 37.5 } } }
        """);

        Assert.Equal(37.5m, extras.ExtraSpend!.Used);
        Assert.Equal(200m, extras.ExtraSpend.Limit);
    }

    [Fact]
    public void Parse_IndividualLimit_WithoutAmounts_IsIgnored()
    {
        var extras = Parse("""
        { "rateLimits": { "individualLimit": { "limit": null, "used": null, "remainingPercent": 100 } } }
        """);

        Assert.Null(extras.ExtraSpend);
    }

    [Fact]
    public void Parse_PrefersTheSingleRateLimitsBucket_OverRateLimitsByLimitId()
    {
        var extras = Parse("""
        {
          "rateLimits": { "planType": "plus", "credits": { "hasCredits": true, "balance": "1" } },
          "rateLimitsByLimitId": {
            "other": { "planType": "team", "credits": { "hasCredits": true, "balance": "999" } }
          }
        }
        """);

        Assert.Equal("Plus", extras.PlanLabel);
        Assert.Equal(1m, extras.CreditsBalance);
    }

    [Fact]
    public void Parse_UsesRateLimitsByLimitId_WhenTheSingleBucketIsAbsent()
    {
        var extras = Parse("""
        {
          "rateLimits": null,
          "rateLimitsByLimitId": {
            "codex": { "planType": "pro", "credits": { "hasCredits": true, "balance": "5" } }
          }
        }
        """);

        Assert.Equal("Pro", extras.PlanLabel);
        Assert.Equal(5m, extras.CreditsBalance);
    }

    [Fact]
    public void Parse_ResetCredits_WithoutDetails_StillReportsTheCount()
    {
        // The app-server omits `credits` when asked to exclude the details.
        var extras = Parse("""
        { "rateLimits": {}, "rateLimitResetCredits": { "availableCount": 3, "credits": null } }
        """);

        Assert.Equal(3, extras.ResetCreditsAvailable);
        Assert.Null(extras.ResetCreditsNextExpiry);
    }

    [Fact]
    public void Parse_ResetCredits_WithNullExpiry_IsIgnoredForTheNextExpiry()
    {
        var extras = Parse("""
        {
          "rateLimits": {},
          "rateLimitResetCredits": {
            "availableCount": 1,
            "credits": [ { "status": "available", "expiresAt": null } ]
          }
        }
        """);

        Assert.Equal(1, extras.ResetCreditsAvailable);
        Assert.Null(extras.ResetCreditsNextExpiry);
    }

    [Fact]
    public void Parse_AbsentResetCredits_ReportsNothing()
    {
        var extras = Parse("""{ "rateLimits": { "planType": "plus" }, "rateLimitResetCredits": null }""");

        Assert.Null(extras.ResetCreditsAvailable);
        Assert.Null(extras.ResetCreditsNextExpiry);
    }

    [Fact]
    public void Parse_EmptyResponse_YieldsEmptyExtras()
        => Assert.True(Parse("{}").IsEmpty);
}
