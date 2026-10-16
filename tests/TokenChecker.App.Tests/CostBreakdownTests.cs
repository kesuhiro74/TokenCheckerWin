using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the per-model cost breakdown shown in the money row's tooltip:
// model-label normalization (dated and undated ids merge), descending order,
// the top-5 cap with the aggregated "other" line, and the JPY conversion.
[Collection("StringsLanguage")]
public class CostBreakdownTests
{
    private const decimal Rate = 150m;

    [Theory]
    [InlineData("claude-opus-5-5-20250514", "claude-opus-5-5")]
    [InlineData("claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("gpt-5.6-sol", "gpt-5.6-sol")]
    // Not a trailing 8-digit date: kept verbatim rather than mangled.
    [InlineData("gpt-4-1234567", "gpt-4-1234567")]
    [InlineData("  claude-sonnet-5-20991231  ", "claude-sonnet-5")]
    public void NormalizeLabel_StripsOnlyTrailingDateSuffix(string modelId, string expected)
        => Assert.Equal(expected, CostBreakdown.NormalizeLabel(modelId));

    [Fact]
    public void NormalizeLabel_BlankId_FallsBackToPlaceholder()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("モデル不明", CostBreakdown.NormalizeLabel("   "));
        Assert.Equal("モデル不明", CostBreakdown.NormalizeLabel(null));
    }

    [Fact]
    public void Build_MergesDatedAndUndatedIds_AndConvertsToJpy()
    {
        var lines = CostBreakdown.Build(
            new Dictionary<string, decimal>
            {
                ["claude-opus-5-5-20250514"] = 2m,
                ["claude-opus-5-5"] = 1m
            },
            Rate);

        var line = Assert.Single(lines);
        Assert.Equal("claude-opus-5-5", line.Label);
        Assert.Equal(3m, line.Cost.Usd);
        Assert.Equal(450m, line.Cost.Jpy);
    }

    [Fact]
    public void Build_OrdersDescending_AndDropsNonPositiveCosts()
    {
        var lines = CostBreakdown.Build(
            new Dictionary<string, decimal>
            {
                ["small"] = 1m,
                ["big"] = 9m,
                ["zero"] = 0m,
                ["negative"] = -5m
            },
            Rate);

        Assert.Equal(new[] { "big", "small" }, lines.Select(line => line.Label));
    }

    [Fact]
    public void Build_MoreThanFiveModels_KeepsTopFivePlusOther()
    {
        Strings.Apply(japanese: true);
        var costs = new Dictionary<string, decimal>();
        for (var i = 1; i <= 8; i++)
        {
            costs[$"model-{i}"] = i;
        }

        var lines = CostBreakdown.Build(costs, Rate);

        Assert.Equal(CostBreakdown.MaxModelLines + 1, lines.Count);
        Assert.Equal(
            new[] { "model-8", "model-7", "model-6", "model-5", "model-4", "その他" },
            lines.Select(line => line.Label));
        // The tail (3 + 2 + 1) is folded into the last line.
        Assert.Equal(6m, lines[^1].Cost.Usd);
        Assert.Equal(900m, lines[^1].Cost.Jpy);
        // The lines still add up to the full month cost (36 USD).
        Assert.Equal(36m, lines.Sum(line => line.Cost.Usd));
    }

    [Fact]
    public void Build_ExactlyFiveModels_HasNoOtherLine()
    {
        var costs = Enumerable.Range(1, 5).ToDictionary(i => $"model-{i}", i => (decimal)i);
        Assert.Equal(5, CostBreakdown.Build(costs, Rate).Count);
    }

    [Fact]
    public void Build_NoCosts_ReturnsEmpty()
    {
        Assert.Empty(CostBreakdown.Build(null, Rate));
        Assert.Empty(CostBreakdown.Build(new Dictionary<string, decimal>(), Rate));
        Assert.Empty(CostBreakdown.Build(new Dictionary<string, decimal> { ["m"] = 0m }, Rate));
    }

    [Theory]
    [InlineData(2, "2")]
    [InlineData(1234, "1,234")]
    public void FormatNumber_IntegralValues_HaveNoDecimals(int value, string expected)
        => Assert.Equal(expected, CostBreakdown.FormatNumber(value));

    [Fact]
    public void FormatNumber_FractionalValue_KeepsTwoDecimals()
        => Assert.Equal("12.34", CostBreakdown.FormatNumber(12.34m));
}
