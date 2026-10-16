using System.Text.Json.Nodes;
using TokenChecker.Core;
using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the parsing of api.anthropic.com/api/oauth/usage against the
// response shape observed on 2026-10-16: the documented five_hour/seven_day
// fields, the newer limits[] array (session / weekly_all / weekly_scoped),
// the legacy seven_day_opus / seven_day_sonnet fields, and the two spending
// blocks (spend, extra_usage).
public sealed class ClaudeUsageResponseParserTests
{
    // Trimmed to the keys we read, with the values actually observed. The
    // obfuscated keys the endpoint also returns are deliberately absent: the
    // parser must never depend on them.
    private const string ObservedResponse = """
    {
      "five_hour": { "utilization": 37, "resets_at": "2026-10-16T12:00:00Z" },
      "seven_day": { "utilization": 52, "resets_at": "2026-10-20T00:00:00Z" },
      "seven_day_opus": null,
      "seven_day_sonnet": null,
      "seven_day_oauth_apps": null,
      "limits": [
        { "kind": "session", "group": "session", "percent": 37, "severity": "normal",
          "resets_at": "2026-10-16T12:00:00Z", "scope": null, "is_active": true },
        { "kind": "weekly_all", "group": "weekly", "percent": 52, "severity": "normal",
          "resets_at": "2026-10-20T00:00:00Z", "scope": null, "is_active": true },
        { "kind": "weekly_scoped", "group": "weekly", "percent": 4, "severity": "normal",
          "resets_at": "2026-10-20T00:00:00Z",
          "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null },
          "is_active": true }
      ],
      "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null,
                       "utilization": null, "currency": null },
      "spend": { "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 },
                 "limit": null, "percent": 0, "enabled": false }
    }
    """;

    private static ClaudeUsageParseResult Parse(string json, string? subscriptionType = null, string? rateLimitTier = null)
        => ClaudeUsageResponseParser.Parse(JsonNode.Parse(json)!, subscriptionType, rateLimitTier);

    [Fact]
    public void Parse_ObservedResponse_ReadsTheDocumentedWindows()
    {
        var result = Parse(ObservedResponse);

        Assert.Equal(2, result.Windows.Count);

        var fiveHour = result.Windows[0];
        Assert.Equal("Claude 5h", fiveHour.Name);
        Assert.Equal(300, fiveHour.WindowDurationMins);
        Assert.Equal(37d, fiveHour.UsedPercent);
        Assert.Equal(100, fiveHour.Limit);
        Assert.Equal(63, fiveHour.Remaining);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), fiveHour.ResetAtUtc);

        var weekly = result.Windows[1];
        Assert.Equal("Claude Weekly", weekly.Name);
        Assert.Equal(10080, weekly.WindowDurationMins);
        Assert.Equal(52d, weekly.UsedPercent);
    }

    [Fact]
    public void Parse_ScopedLimits_AreNeverMixedIntoWindows()
    {
        // The App finds the weekly window by "first window with
        // WindowDurationMins == 10080", so a scoped weekly entry in Windows
        // would be displayed as THE weekly limit.
        var result = Parse(ObservedResponse);

        Assert.DoesNotContain(result.Windows, window => window.Name == "Fable");
        Assert.Equal(2, result.Windows.Count(window => window.WindowDurationMins is 300 or 10080));

        var scoped = Assert.Single(result.Extras.ScopedLimits!);
        Assert.Equal("Fable", scoped.Label);
        Assert.Equal(4d, scoped.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), scoped.ResetAtUtc);
    }

    [Fact]
    public void Parse_ObservedResponse_HasNoExtraSpend()
    {
        // spend.enabled and extra_usage.is_enabled were both false.
        Assert.Null(Parse(ObservedResponse).Extras.ExtraSpend);
    }

    [Fact]
    public void Parse_FallsBackToLimitsArray_WhenTheDocumentedFieldsAreMissing()
    {
        // Insurance against the endpoint dropping five_hour/seven_day.
        var result = Parse("""
        {
          "five_hour": null,
          "seven_day": null,
          "limits": [
            { "kind": "session", "percent": 11, "resets_at": "2026-10-16T12:00:00Z" },
            { "kind": "weekly_all", "percent": 22, "resets_at": "2026-10-20T00:00:00Z" }
          ]
        }
        """);

        Assert.Equal(2, result.Windows.Count);
        Assert.Equal("Claude 5h", result.Windows[0].Name);
        Assert.Equal(11d, result.Windows[0].UsedPercent);
        Assert.Equal(300, result.Windows[0].WindowDurationMins);
        Assert.Equal("Claude Weekly", result.Windows[1].Name);
        Assert.Equal(22d, result.Windows[1].UsedPercent);
        Assert.Equal(10080, result.Windows[1].WindowDurationMins);
    }

    [Fact]
    public void Parse_DocumentedFieldsWin_OverTheLimitsArray()
    {
        var result = Parse("""
        {
          "five_hour": { "utilization": 37, "resets_at": "2026-10-16T12:00:00Z" },
          "limits": [ { "kind": "session", "percent": 99 } ]
        }
        """);

        Assert.Equal(37d, Assert.Single(result.Windows).UsedPercent);
    }

    [Fact]
    public void Parse_NoWindowsAtAll_ReturnsNoWindows()
    {
        var result = Parse("""{ "five_hour": null, "seven_day": null }""");

        Assert.Empty(result.Windows);
    }

    [Fact]
    public void Parse_ScopedLimit_FallsBackToTheSurfaceDisplayName()
    {
        var result = Parse("""
        {
          "limits": [
            { "kind": "weekly_scoped", "percent": 7,
              "scope": { "model": null, "surface": { "display_name": "Claude Code" } } }
          ]
        }
        """);

        Assert.Equal("Claude Code", Assert.Single(result.Extras.ScopedLimits!).Label);
    }

    [Fact]
    public void Parse_ScopedLimit_WithoutAnyDisplayName_IsSkipped()
    {
        // Nothing to label it with, so it is dropped rather than invented.
        var result = Parse("""
        {
          "limits": [ { "kind": "weekly_scoped", "percent": 7, "scope": { "model": null, "surface": null } } ]
        }
        """);

        Assert.Null(result.Extras.ScopedLimits);
    }

    [Fact]
    public void Parse_LegacyPerModelFields_AreUsedOnlyWithoutScopedLimits()
    {
        var result = Parse("""
        {
          "seven_day_opus": { "utilization": 18, "resets_at": "2026-10-20T00:00:00Z" },
          "seven_day_sonnet": { "utilization": 5, "resets_at": "2026-10-20T00:00:00Z" },
          "limits": []
        }
        """);

        var scoped = result.Extras.ScopedLimits!;
        Assert.Equal(2, scoped.Count);
        Assert.Equal("Opus", scoped[0].Label);
        Assert.Equal(18d, scoped[0].UsedPercent);
        Assert.Equal("Sonnet", scoped[1].Label);
        Assert.Equal(5d, scoped[1].UsedPercent);

        // And they must not leak into Windows either.
        Assert.Empty(result.Windows);
    }

    [Fact]
    public void Parse_ScopedLimits_SuppressTheLegacyPerModelFields()
    {
        var result = Parse("""
        {
          "seven_day_opus": { "utilization": 18 },
          "limits": [
            { "kind": "weekly_scoped", "percent": 4,
              "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } }
          ]
        }
        """);

        var scoped = Assert.Single(result.Extras.ScopedLimits!);
        Assert.Equal("Fable", scoped.Label);
    }

    [Fact]
    public void Parse_Spend_ConvertsMinorUnitsWithTheReportedExponent()
    {
        var result = Parse("""
        {
          "spend": {
            "used": { "amount_minor": 1234, "currency": "USD", "exponent": 2 },
            "limit": { "amount_minor": 5000, "currency": "USD", "exponent": 2 },
            "enabled": true
          }
        }
        """);

        var spend = result.Extras.ExtraSpend!;
        Assert.Equal(12.34m, spend.Used);
        Assert.Equal(50m, spend.Limit);
        Assert.Equal("USD", spend.Unit);
    }

    [Fact]
    public void Parse_Spend_WithANonObjectLimit_LeavesTheLimitNull()
    {
        // The populated shape of spend.limit is unconfirmed, so anything that
        // is not an object carrying amount_minor must not become a number.
        var result = Parse("""
        {
          "spend": {
            "used": { "amount_minor": 250, "currency": "JPY", "exponent": 0 },
            "limit": 5000,
            "enabled": true
          }
        }
        """);

        var spend = result.Extras.ExtraSpend!;
        Assert.Equal(250m, spend.Used);
        Assert.Null(spend.Limit);
        Assert.Equal("JPY", spend.Unit);
    }

    [Fact]
    public void Parse_Spend_DisabledFallsBackToExtraUsageCents()
    {
        var result = Parse("""
        {
          "spend": { "used": { "amount_minor": 9999, "currency": "USD", "exponent": 2 }, "enabled": false },
          "extra_usage": { "is_enabled": true, "monthly_limit": 5000, "used_credits": 125, "currency": null }
        }
        """);

        var spend = result.Extras.ExtraSpend!;
        Assert.Equal(1.25m, spend.Used);
        Assert.Equal(50m, spend.Limit);
        Assert.Equal("USD", spend.Unit);
    }

    [Fact]
    public void Parse_ExtraUsage_WithNullAmounts_ReportsZeroUsedAndNoLimit()
    {
        var result = Parse("""
        { "extra_usage": { "is_enabled": true, "monthly_limit": null, "used_credits": null } }
        """);

        var spend = result.Extras.ExtraSpend!;
        Assert.Equal(0m, spend.Used);
        Assert.Null(spend.Limit);
    }

    [Fact]
    public void Parse_BothSpendBlocksDisabled_ReportsNoExtraSpend()
    {
        var result = Parse("""
        {
          "spend": { "used": { "amount_minor": 500, "exponent": 2 }, "enabled": false },
          "extra_usage": { "is_enabled": false, "used_credits": 500 }
        }
        """);

        Assert.Null(result.Extras.ExtraSpend);
    }

    [Fact]
    public void Parse_PassesThroughThePlanLabel()
    {
        var result = Parse(ObservedResponse, "max", "default_claude_max_5x");

        Assert.Equal("Max 5x", result.Extras.PlanLabel);
    }

    [Fact]
    public void Parse_ClampsOutOfRangePercentages()
    {
        var result = Parse("""
        { "five_hour": { "utilization": 142 }, "seven_day": { "utilization": -5 } }
        """);

        Assert.Equal(100d, result.Windows[0].UsedPercent);
        Assert.Equal(0, result.Windows[0].Remaining);
        Assert.Equal(0d, result.Windows[1].UsedPercent);
        Assert.Equal(100, result.Windows[1].Remaining);
    }

    [Fact]
    public void Parse_EmptyObject_YieldsNoWindowsAndEmptyExtras()
    {
        var result = Parse("{}");

        Assert.Empty(result.Windows);
        Assert.True(result.Extras.IsEmpty);
    }

    [Fact]
    public void Parse_IgnoresUnknownAndObfuscatedKeys()
    {
        // The endpoint also returns unstable obfuscated keys; they must never
        // affect the result.
        var result = Parse("""
        {
          "five_hour": { "utilization": 10 },
          "iguana_necktie": { "utilization": 99, "resets_at": "2026-10-16T12:00:00Z" },
          "walrus_tuba": [ { "kind": "weekly_all", "percent": 98 } ]
        }
        """);

        Assert.Equal(10d, Assert.Single(result.Windows).UsedPercent);
        Assert.Null(result.Extras.ScopedLimits);
    }

    [Fact]
    public void Parse_ScopedLimits_AreNotFilteredByIsActive()
    {
        // The meaning of is_active is undocumented; dropping an entry would
        // hide a limit the user is actually subject to.
        var result = Parse("""
        {
          "limits": [
            { "kind": "weekly_scoped", "percent": 4, "is_active": false,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """);

        Assert.Equal("Fable", Assert.Single(result.Extras.ScopedLimits!).Label);
    }
}
