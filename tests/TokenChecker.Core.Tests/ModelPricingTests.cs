using System.Globalization;
using TokenChecker.Core.LocalCost;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the pricing table lookup (longest-prefix matching, normalization,
// unknown-model handling) and the decimal cost formula used by the local
// daily-cost readers.
public class ModelPricingTests
{
    [Theory]
    [InlineData("claude-opus-4-8", "5", "25")]
    [InlineData("claude-sonnet-4-6", "3", "15")]
    [InlineData("gpt-5.5", "5", "30")]
    // gpt-5.1-codex has no dedicated entry and must fall back to "gpt-5".
    [InlineData("gpt-5.1-codex", "1.25", "10")]
    // The legacy full id is caught by the "claude-opus-4-2" prefix entry.
    [InlineData("claude-opus-4-20250514", "15", "75")]
    public void Find_ResolvesByPrefix(string modelId, string expectedInput, string expectedOutput)
    {
        var price = ModelPricing.Find(modelId);

        Assert.NotNull(price);
        Assert.Equal(decimal.Parse(expectedInput, CultureInfo.InvariantCulture), price.InputUsdPerMTok);
        Assert.Equal(decimal.Parse(expectedOutput, CultureInfo.InvariantCulture), price.OutputUsdPerMTok);
    }

    [Fact]
    public void Find_LongestPrefixWins()
    {
        // "gpt-5.4-mini" matches both "gpt-5.4" and "gpt-5.4-mini"; the longer
        // (more specific) prefix must win.
        var price = ModelPricing.Find("gpt-5.4-mini-2026-01-01");

        Assert.NotNull(price);
        Assert.Equal("gpt-5.4-mini", price.IdPrefix);
        Assert.Equal(0.75m, price.InputUsdPerMTok);
        Assert.Equal(4.5m, price.OutputUsdPerMTok);
    }

    [Fact]
    public void Find_NormalizesCaseAndWhitespace()
    {
        var price = ModelPricing.Find("  Claude-Opus-4-8  ");

        Assert.NotNull(price);
        Assert.Equal("claude-opus-4-8", price.IdPrefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<synthetic>")]
    [InlineData("totally-unknown-model")]
    public void Find_UnknownOrEmpty_ReturnsNull(string? modelId)
        => Assert.Null(ModelPricing.Find(modelId));

    [Fact]
    public void Cost_ExactlyOneMillionInputTokens_EqualsInputRate()
    {
        var price = ModelPricing.Find("claude-fable-5");

        Assert.NotNull(price);
        Assert.Equal(price.InputUsdPerMTok, ModelPricing.Cost(price, 1_000_000, 0, 0, 0));
    }

    [Fact]
    public void Cost_MixedTokens_ComputesInDecimal()
    {
        var price = ModelPricing.Find("claude-opus-4-8");
        Assert.NotNull(price);

        // (12*5 + 234*25 + 3456*6.25 + 78901*0.5) / 1e6 = 66960.5 / 1e6.
        var cost = ModelPricing.Cost(price, 12, 234, 3456, 78901);

        Assert.Equal(0.0669605m, cost);
    }

    [Fact]
    public void Cost_ZeroTokens_IsZero()
    {
        var price = ModelPricing.Find("gpt-5");
        Assert.NotNull(price);

        Assert.Equal(0m, ModelPricing.Cost(price, 0, 0, 0, 0));
    }
}
