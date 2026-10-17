using System.Text.Json.Nodes;
using TokenChecker.Core.Providers.GitHubCopilot;
using Xunit;
using static TokenChecker.Core.Providers.GitHubCopilot.GitHubBillingUsageParser;

namespace TokenChecker.Core.Tests;

// Locks down the pure AI-credit classification + credit math used by the GitHub
// Copilot provider (no network — operates on JsonNode / item records).
public class GitHubBillingUsageParserTests
{
    private static GitHubBillingUsageItem Item(
        string? product = null, string? sku = null, string? unitType = null,
        decimal? quantity = null, decimal? grossQuantity = null, decimal? grossAmount = null)
        => new(product, sku, unitType, quantity, grossQuantity, grossAmount, null, null);

    [Theory]
    [InlineData("AI Credits", "aicredits")]
    [InlineData("ai-credits", "aicredits")]
    [InlineData("ai_credit", "aicredit")]
    [InlineData("Premium Request", "premiumrequest")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_LowercasesAndStrips(string? input, string expected)
        => Assert.Equal(expected, Normalize(input));

    [Fact]
    public void PremiumRequest_ExcludedByUnitType()
        => Assert.False(IsCopilotAiCreditUsage(Item(unitType: "requests", grossQuantity: 5m)));

    [Fact]
    public void PremiumRequest_ExcludedFirst_EvenWithAiCreditUnit()
        => Assert.False(IsCopilotAiCreditUsage(Item(sku: "copilot_premium_request", unitType: "AI Credits")));

    [Fact]
    public void Strong_UnitAiCredits_True()
        => Assert.True(IsCopilotAiCreditUsage(Item(unitType: "AI Credits")));

    [Fact]
    public void Strong_SkuAiCredit_True()
        => Assert.True(IsCopilotAiCreditUsage(Item(sku: "AI_CREDIT_BUCKET")));

    [Fact]
    public void Strong_ProductAiCredit_True()
        => Assert.True(IsCopilotAiCreditUsage(Item(product: "Copilot AI Credit")));

    [Fact]
    public void Weak_GenericCredits_WithCopilotProduct_True()
        => Assert.True(IsCopilotAiCreditUsage(Item(product: "GitHub Copilot", unitType: "credits")));

    [Fact]
    public void Weak_GenericCredits_WithoutContext_False()
        => Assert.False(IsCopilotAiCreditUsage(Item(product: "GitHub Actions", unitType: "credits")));

    [Fact]
    public void Unrelated_Row_False()
        => Assert.False(IsCopilotAiCreditUsage(Item(product: "Codespaces", unitType: "minutes")));

    [Fact]
    public void CreditsForRow_CreditsUnit_UsesGrossQuantity()
        => Assert.Equal(42.5m, CreditsForRow(Item(unitType: "credits", grossQuantity: 42.5m)));

    [Fact]
    public void CreditsForRow_FallsBackToGrossAmountTimes100()
        => Assert.Equal(123m, CreditsForRow(Item(unitType: "other", grossAmount: 1.23m)));

    [Fact]
    public void CreditsForRow_LastResort_GrossThenQuantityThenZero()
    {
        Assert.Equal(10m, CreditsForRow(Item(unitType: "other", quantity: 10m)));
        Assert.Equal(0m, CreditsForRow(Item(unitType: "other")));
    }

    [Fact]
    public void CreditTotal_SumsInDecimal_RoundsOnce_AwayFromZero()
    {
        var items = new[]
        {
            Item(unitType: "credits", grossQuantity: 1.25m),
            Item(unitType: "credits", grossQuantity: 1.25m),
        };
        var sum = items.Sum(CreditsForRow);                          // exactly 2.50 in decimal
        var rounded = (long)Math.Round(sum, MidpointRounding.AwayFromZero);
        Assert.Equal(3L, rounded);                                   // away-from-zero, not banker's 2
    }

    [Fact]
    public void TryParseUsageItems_ValidArray_True()
    {
        var root = JsonNode.Parse("""{"usageItems":[{"product":"Copilot","sku":"x","unitType":"AI Credits","grossQuantity":5}]}""");
        Assert.True(TryParseUsageItems(root, out var items));
        Assert.Single(items);
        Assert.Equal("Copilot", items[0].Product);
        Assert.Equal(5m, items[0].GrossQuantity);
    }

    [Fact]
    public void TryParseUsageItems_Absent_False()
    {
        Assert.False(TryParseUsageItems(JsonNode.Parse("""{"other":1}"""), out var items));
        Assert.Empty(items);
    }

    [Fact]
    public void TryParseUsageItems_NotArray_False()
    {
        Assert.False(TryParseUsageItems(JsonNode.Parse("""{"usageItems":{"a":1}}"""), out var items));
        Assert.Empty(items);
    }

    [Fact]
    public void ParseUsageItems_Absent_ReturnsEmpty()
        => Assert.Empty(ParseUsageItems(JsonNode.Parse("{}")));

    [Fact]
    public void TryGetLogin_Present_True()
    {
        Assert.True(TryGetLogin(JsonNode.Parse("""{"login":"octocat"}"""), out var login));
        Assert.Equal("octocat", login);
    }

    [Fact]
    public void TryGetLogin_MissingOrBlank_False()
    {
        Assert.False(TryGetLogin(JsonNode.Parse("{}"), out _));
        Assert.False(TryGetLogin(JsonNode.Parse("""{"login":"  "}"""), out _));
    }

    // ----- model / discount / net parsing ---------------------------------

    [Fact]
    public void TryParseUsageItems_CapturesModelDiscountAndNet()
    {
        var root = JsonNode.Parse("""
        {"usageItems":[{
            "product":"Copilot","sku":"ai_credit","model":"gpt-5.6","unitType":"AI Credits",
            "grossQuantity":100,"grossAmount":1.0,
            "discountQuantity":80,"discountAmount":0.8,
            "netQuantity":20,"netAmount":0.2}]}
        """);

        Assert.True(TryParseUsageItems(root, out var items));
        var item = Assert.Single(items);
        Assert.Equal("gpt-5.6", item.Model);
        Assert.Equal(80m, item.DiscountQuantity);
        Assert.Equal(0.8m, item.DiscountAmount);
        Assert.Equal(20m, item.NetQuantity);
        Assert.Equal(0.2m, item.NetAmount);
    }

    [Fact]
    public void TryParseUsageItems_MissingModel_IsNull()
    {
        // The fallback /settings/billing/usage endpoint has no "model" field.
        var root = JsonNode.Parse("""{"usageItems":[{"product":"Copilot","unitType":"credits","grossQuantity":5}]}""");

        Assert.True(TryParseUsageItems(root, out var items));
        Assert.Null(items[0].Model);
        Assert.Null(items[0].DiscountQuantity);
        Assert.Null(items[0].DiscountAmount);
    }

    [Fact]
    public void TryParseUsageItems_ExplicitNullModel_IsNull()
    {
        var root = JsonNode.Parse("""{"usageItems":[{"model":null,"unitType":"credits","grossQuantity":5}]}""");

        Assert.True(TryParseUsageItems(root, out var items));
        Assert.Null(items[0].Model);
    }

    // ----- BuildExtras ----------------------------------------------------

    private static GitHubBillingUsageItem CreditRow(string? model, decimal credits, decimal? netAmount = null)
        => new("GitHub Copilot", "ai_credit", "AI Credits", null, credits, null, null, netAmount, model);

    [Fact]
    public void BuildExtras_GroupsCreditsByModel()
    {
        var extras = BuildExtras(new[]
        {
            CreditRow("gpt-5.6", 100m),
            CreditRow(" gpt-5.6 ", 50.5m),
            CreditRow("claude-opus-5", 20m)
        });

        var models = extras?.ModelCredits;
        Assert.NotNull(models);
        Assert.Equal(2, models!.Count);
        Assert.Equal("gpt-5.6", models[0].Label);
        Assert.Equal(150.5m, models[0].Credits);
        Assert.Equal("claude-opus-5", models[1].Label);
        Assert.Equal(20m, models[1].Credits);
    }

    [Fact]
    public void BuildExtras_NoRowHasModel_NoModelList()
    {
        var extras = BuildExtras(new[] { CreditRow(null, 10m), CreditRow("  ", 5m) });

        // Nothing but unknown-model rows: a single "unknown" bucket would tell
        // the user nothing, so no list at all (and the whole extras is empty).
        Assert.Null(extras);
    }

    [Fact]
    public void BuildExtras_NullModelRows_GroupedUnderNullLabel()
    {
        var extras = BuildExtras(new[]
        {
            CreditRow("gpt-5.6", 100m),
            CreditRow(null, 10m),
            CreditRow("", 5m)
        });

        var models = extras?.ModelCredits;
        Assert.NotNull(models);
        Assert.Equal(2, models!.Count);
        Assert.Equal("gpt-5.6", models[0].Label);
        Assert.Null(models[1].Label);
        Assert.Equal(15m, models[1].Credits);
    }

    [Fact]
    public void BuildExtras_NoNetAmount_NoSpend()
    {
        var extras = BuildExtras(new[] { CreditRow("gpt-5.6", 100m) });

        Assert.NotNull(extras);
        Assert.Null(extras!.ExtraSpend);
    }

    [Fact]
    public void BuildExtras_NetAmountZero_SpendIsZeroUsd()
    {
        // 0 is a real answer ("the allowance covered everything"), not "absent".
        var extras = BuildExtras(new[] { CreditRow("gpt-5.6", 100m, netAmount: 0m) });

        Assert.Equal(0m, extras?.ExtraSpend?.Used);
        Assert.Null(extras?.ExtraSpend?.Limit);
        Assert.Equal("USD", extras?.ExtraSpend?.Unit);
    }

    [Fact]
    public void BuildExtras_SumsNetAmountInDecimal()
    {
        var extras = BuildExtras(new[]
        {
            CreditRow("gpt-5.6", 100m, netAmount: 0.1m),
            CreditRow("claude-opus-5", 50m, netAmount: 0.2m),
            CreditRow("gemini", 25m)
        });

        Assert.Equal(0.3m, extras?.ExtraSpend?.Used);
    }

    [Fact]
    public void BuildExtras_NothingToShow_ReturnsNull()
    {
        Assert.Null(BuildExtras(Array.Empty<GitHubBillingUsageItem>()));
        Assert.Null(BuildExtras(null));
    }

    [Fact]
    public void BuildExtras_SpendOnly_StillReturnsExtras()
    {
        var extras = BuildExtras(new[] { CreditRow(null, 10m, netAmount: 1.5m) });

        Assert.NotNull(extras);
        Assert.Null(extras!.ModelCredits);
        Assert.Equal(1.5m, extras.ExtraSpend?.Used);
    }

    // ----- date parsing ---------------------------------------------------

    [Fact]
    public void TryParseUsageItems_CapturesDate()
    {
        // The real shape: one row per UTC day, timestamp with a Z suffix and no
        // fractional seconds.
        var root = JsonNode.Parse("""
        {"usageItems":[{"date":"2026-10-07T00:31:53Z","product":"copilot",
            "sku":"Copilot AI Credits","unitType":"AICredits","grossAmount":18.259207015}]}
        """);

        Assert.True(TryParseUsageItems(root, out var items));
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 0, 31, 53, TimeSpan.Zero), items[0].Date);
    }

    [Fact]
    public void TryParseUsageItems_DateWithOffset_KeepsTheInstant()
    {
        var root = JsonNode.Parse("""{"usageItems":[{"date":"2026-10-07T08:00:00+09:00"}]}""");

        Assert.True(TryParseUsageItems(root, out var items));
        // 08:00 JST is still 2026-10-06 in UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 23, 0, 0, TimeSpan.Zero), items[0].Date!.Value.ToUniversalTime());
    }

    [Fact]
    public void TryParseUsageItems_DateWithoutOffset_IsTreatedAsUtc()
    {
        var root = JsonNode.Parse("""{"usageItems":[{"date":"2026-10-07T00:31:53"}]}""");

        Assert.True(TryParseUsageItems(root, out var items));
        Assert.Equal(TimeSpan.Zero, items[0].Date!.Value.Offset);
    }

    [Theory]
    [InlineData("""{"usageItems":[{"date":"not a date"}]}""")]
    [InlineData("""{"usageItems":[{"date":null}]}""")]
    [InlineData("""{"usageItems":[{"product":"copilot"}]}""")]
    public void TryParseUsageItems_UnusableDate_IsNull(string json)
    {
        Assert.True(TryParseUsageItems(JsonNode.Parse(json), out var items));
        Assert.Null(items[0].Date);
    }

    // ----- GroupDailyCredits ----------------------------------------------

    private static GitHubBillingUsageItem DatedRow(string? date, decimal credits)
        => new(
            "copilot", "Copilot AI Credits", "AICredits",
            null, credits, null, null, null, null, null, null,
            date is null
                ? null
                : DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void GroupDailyCredits_SumsPerUtcDay_Ascending()
    {
        var daily = GroupDailyCredits(new[]
        {
            DatedRow("2026-10-09T10:00:00Z", 5m),
            DatedRow("2026-10-07T00:31:53Z", 1.5m),
            DatedRow("2026-10-07T23:59:59Z", 2.25m)
        });

        Assert.NotNull(daily);
        Assert.Equal(2, daily!.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), daily[0].UtcDate);
        Assert.Equal(3.75m, daily[0].Credits);
        Assert.Equal(new DateOnly(2026, 10, 9), daily[1].UtcDate);
        Assert.Equal(5m, daily[1].Credits);
    }

    [Fact]
    public void GroupDailyCredits_UtcDayBoundary_SplitsTheRows()
    {
        // 23:59:59Z and the 00:00:00Z one second later are DIFFERENT days.
        var daily = GroupDailyCredits(new[]
        {
            DatedRow("2026-10-07T23:59:59Z", 10m),
            DatedRow("2026-10-08T00:00:00Z", 20m)
        });

        Assert.Equal(2, daily!.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), daily[0].UtcDate);
        Assert.Equal(10m, daily[0].Credits);
        Assert.Equal(new DateOnly(2026, 10, 8), daily[1].UtcDate);
        Assert.Equal(20m, daily[1].Credits);
    }

    [Fact]
    public void GroupDailyCredits_NonUtcOffset_LandsOnTheUtcDay()
    {
        // 2026-10-08 08:00 +09:00 == 2026-10-07 23:00 UTC -> the 7th.
        var daily = GroupDailyCredits(new[] { DatedRow("2026-10-08T08:00:00+09:00", 7m) });

        Assert.Equal(new DateOnly(2026, 10, 7), Assert.Single(daily!).UtcDate);
    }

    [Fact]
    public void GroupDailyCredits_UndatedRows_AreSkipped()
    {
        var daily = GroupDailyCredits(new[]
        {
            DatedRow(null, 99m),
            DatedRow("2026-10-07T00:31:53Z", 1m)
        });

        var day = Assert.Single(daily!);
        Assert.Equal(new DateOnly(2026, 10, 7), day.UtcDate);
        Assert.Equal(1m, day.Credits);
    }

    [Fact]
    public void GroupDailyCredits_NoDatedRow_ReturnsNull()
    {
        Assert.Null(GroupDailyCredits(new[] { DatedRow(null, 10m) }));
        Assert.Null(GroupDailyCredits(Array.Empty<GitHubBillingUsageItem>()));
        Assert.Null(GroupDailyCredits(null));
    }

    // ----- Merge ----------------------------------------------------------

    private static readonly IReadOnlyList<DailyCredits> SampleDaily =
        new[] { new DailyCredits(new DateOnly(2026, 10, 7), 1826m) };

    [Fact]
    public void Merge_DailyOnly_CreatesExtras()
    {
        var merged = Merge(null, SampleDaily);

        Assert.NotNull(merged);
        Assert.Same(SampleDaily, merged!.DailyCredits);
        Assert.Null(merged.ModelCredits);
        Assert.Null(merged.ExtraSpend);
    }

    [Fact]
    public void Merge_KeepsTheExistingExtras()
    {
        var extras = BuildExtras(new[] { CreditRow("gpt-5.6", 100m, netAmount: 1.5m) })!;

        var merged = Merge(extras, SampleDaily);

        Assert.Equal("gpt-5.6", merged!.ModelCredits![0].Label);
        Assert.Equal(1.5m, merged.ExtraSpend?.Used);
        Assert.Same(SampleDaily, merged.DailyCredits);
    }

    [Fact]
    public void Merge_NoDaily_LeavesTheExtrasUntouched()
    {
        var extras = BuildExtras(new[] { CreditRow("gpt-5.6", 100m) })!;

        Assert.Same(extras, Merge(extras, null));
        Assert.Same(extras, Merge(extras, Array.Empty<DailyCredits>()));
        Assert.Null(Merge(null, null));
        Assert.Null(Merge(null, Array.Empty<DailyCredits>()));
    }
}
