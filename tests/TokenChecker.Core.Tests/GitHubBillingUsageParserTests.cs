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
}
