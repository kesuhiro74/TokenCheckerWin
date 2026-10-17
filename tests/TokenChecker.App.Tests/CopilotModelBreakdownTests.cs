using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the Copilot card's "this month by model" block: merging, ordering,
// the top-5 cap with the aggregated "other" row, the unknown-model label, and
// the share percentage / value text.
//
// Shares the StringsLanguage collection because the labels and the value text go
// through Strings (which flips a global).
[Collection("StringsLanguage")]
public class CopilotModelBreakdownTests
{
    private static ModelCredits Row(string? label, decimal credits) => new(label, credits);

    [Fact]
    public void Build_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(CopilotModelBreakdown.Build(null));
        Assert.Empty(CopilotModelBreakdown.Build(Array.Empty<ModelCredits>()));
    }

    [Fact]
    public void Build_DropsZeroAndNegative()
    {
        var lines = CopilotModelBreakdown.Build(new[]
        {
            Row("a", 0m),
            Row("b", -5m),
            Row("c", 10m)
        });

        var line = Assert.Single(lines);
        Assert.Equal("c", line.Label);
    }

    [Fact]
    public void Build_AllDropped_ReturnsEmpty()
        => Assert.Empty(CopilotModelBreakdown.Build(new[] { Row("a", 0m), Row("b", -1m) }));

    [Fact]
    public void Build_MergesEqualLabels_AndSortsDescending()
    {
        var lines = CopilotModelBreakdown.Build(new[]
        {
            Row("small", 10m),
            Row("big", 40m),
            Row("small", 30m)
        });

        Assert.Equal(new[] { "big", "small" }, lines.Select(l => l.Label));
        Assert.Equal(40m, lines[0].Credits);
        Assert.Equal(40m, lines[1].Credits);
    }

    [Fact]
    public void Build_EqualCredits_TieBreakOnLabel()
    {
        var lines = CopilotModelBreakdown.Build(new[] { Row("zeta", 10m), Row("alpha", 10m) });

        Assert.Equal(new[] { "alpha", "zeta" }, lines.Select(l => l.Label));
    }

    [Fact]
    public void Build_NullLabel_BecomesUnknownModel()
    {
        Strings.Apply(japanese: true);
        var lines = CopilotModelBreakdown.Build(new[] { Row(null, 10m), Row("   ", 5m) });

        // Both blank buckets merge into the one "unknown model" row.
        var line = Assert.Single(lines);
        Assert.Equal("モデル不明", line.Label);
        Assert.Equal(15m, line.Credits);
    }

    [Fact]
    public void Build_CapsAtFiveModelsPlusOther()
    {
        Strings.Apply(japanese: true);
        var lines = CopilotModelBreakdown.Build(new[]
        {
            Row("m1", 100m), Row("m2", 90m), Row("m3", 80m), Row("m4", 70m),
            Row("m5", 60m), Row("m6", 50m), Row("m7", 40m)
        });

        Assert.Equal(CopilotModelBreakdown.MaxModelLines + 1, lines.Count);
        Assert.Equal(new[] { "m1", "m2", "m3", "m4", "m5", "その他" }, lines.Select(l => l.Label));
        Assert.Equal(90m, lines[^1].Credits);
    }

    [Fact]
    public void Build_ExactlyFiveModels_NoOtherRow()
    {
        Strings.Apply(japanese: true);
        var lines = CopilotModelBreakdown.Build(new[]
        {
            Row("m1", 5m), Row("m2", 4m), Row("m3", 3m), Row("m4", 2m), Row("m5", 1m)
        });

        Assert.Equal(5, lines.Count);
        Assert.DoesNotContain(lines, l => l.Label == Strings.T("その他"));
    }

    [Fact]
    public void Build_SharesAreOfTheShownTotal()
    {
        var lines = CopilotModelBreakdown.Build(new[] { Row("a", 75m), Row("b", 25m) });

        Assert.Equal(75d, lines[0].SharePercent, 6);
        Assert.Equal(25d, lines[1].SharePercent, 6);
        Assert.Equal(100d, lines.Sum(l => l.SharePercent), 6);
    }

    [Fact]
    public void ValueText_Japanese_RoundsCreditsAndShare()
    {
        Strings.Apply(japanese: true);
        var lines = CopilotModelBreakdown.Build(new[] { Row("a", 1234.5m), Row("b", 3500m) });

        // 1234.5 -> 1,235 (away from zero); 1234.5 / 4734.5 = 26.07% -> 26%.
        Assert.Equal("1,235（26%）", CopilotModelBreakdown.ValueText(lines[1]));
    }

    [Fact]
    public void ValueText_TinyNonZeroShare_ReadsLessThanOne()
    {
        Strings.Apply(japanese: true);
        var lines = CopilotModelBreakdown.Build(new[] { Row("big", 9148.6m), Row("tiny", 3.57m) });

        // 3.57 / 9152.17 = 0.04% -> rounds to 0, but the model was used.
        Assert.Equal("4（<1%）", CopilotModelBreakdown.ValueText(lines[1]));
    }

    [Fact]
    public void ValueText_English_UsesAsciiParentheses()
    {
        Strings.Apply(japanese: false);
        try
        {
            var line = Assert.Single(CopilotModelBreakdown.Build(new[] { Row("a", 1000m) }));
            Assert.Equal("1,000 (100%)", CopilotModelBreakdown.ValueText(line));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }
}
