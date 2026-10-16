using System.Globalization;
using Xunit;

namespace TokenChecker.App.Tests;

// The daily-spend label follows the UI language: yen in Japanese, US dollars in
// English. Shares the StringsLanguage collection so flipping the global language
// flag does not race other language-sensitive tests.
[Collection("StringsLanguage")]
public class DailyCostTextTests
{
    [Fact]
    public void Format_Japanese_ShowsYenNoDecimals()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        Strings.Apply(japanese: true);
        try
        {
            Assert.Equal("¥1,235 (daily)", DailyCostText.Format(new DailyCost(8.5m, 1234.5m)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Format_English_ShowsDollarsWithCents()
    {
        Strings.Apply(japanese: false);
        try
        {
            // USD value drives the dollar form; the JPY value is ignored.
            Assert.Equal("$12.34 (daily)", DailyCostText.Format(new DailyCost(12.34m, 1900m)));
            Assert.Equal("$1,234.50 (daily)", DailyCostText.Format(new DailyCost(1234.5m, 190000m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Amount_FollowsTheUiLanguage()
    {
        WithJapaneseCulture(() =>
        {
            Strings.Apply(japanese: true);
            Assert.Equal("¥1,898", DailyCostText.Amount(new DailyCost(12.6m, 1898m)));
        });

        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("$12.60", DailyCostText.Amount(new DailyCost(12.6m, 1898m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Labels_FollowTheUiLanguage()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("今日", DailyCostText.TodayLabel);
        Assert.Equal("今月", DailyCostText.MonthLabel);

        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("Today", DailyCostText.TodayLabel);
            Assert.Equal("This month", DailyCostText.MonthLabel);
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void FooterTotal_IsFlaggedAsAnApiEstimate()
    {
        WithJapaneseCulture(() =>
        {
            Strings.Apply(japanese: true);
            Assert.Equal("今月合計（API換算） ¥5,000", DailyCostText.FooterTotal(new DailyCost(33.3m, 5000m)));
        });

        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("Month total (API est.) $33.30", DailyCostText.FooterTotal(new DailyCost(33.3m, 5000m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void MinimumCost_WithMonth_AppendsTheMonthlyTotal()
    {
        WithJapaneseCulture(() =>
        {
            Strings.Apply(japanese: true);
            Assert.Equal(
                "¥1,234 / 月¥23,456",
                DailyCostText.MinimumCost(new DailyCost(8m, 1234m), new DailyCost(156m, 23456m)));
        });

        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal(
                "$12.34 / mo $123.45",
                DailyCostText.MinimumCost(new DailyCost(12.34m, 1900m), new DailyCost(123.45m, 19000m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void MinimumCost_WithoutMonth_KeepsTheTodayOnlyForm()
    {
        WithJapaneseCulture(() =>
        {
            Strings.Apply(japanese: true);
            Assert.Equal("¥1,234 (daily)", DailyCostText.MinimumCost(new DailyCost(8m, 1234m), null));
        });
    }

    [Fact]
    public void MonthBreakdownTooltip_ListsModelsAndTheEstimateNote()
    {
        WithJapaneseCulture(() =>
        {
            Strings.Apply(japanese: true);
            var tip = DailyCostText.MonthBreakdownTooltip(new[]
            {
                new ModelCostLine("claude-opus-5-5", new DailyCost(5.9m, 890m)),
                new ModelCostLine("その他", new DailyCost(1m, 150m))
            });

            var lines = tip.Split(Environment.NewLine);
            Assert.Equal("今月のモデル別内訳", lines[0]);
            Assert.Equal("claude-opus-5-5  ¥890", lines[1]);
            Assert.Equal("その他  ¥150", lines[2]);
            Assert.Equal("API換算の推定額（ローカルログから計算）", lines[^1]);
        });
    }

    [Fact]
    public void MonthBreakdownTooltip_NoBreakdown_IsJustTheNote()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(
            "API換算の推定額（ローカルログから計算）",
            DailyCostText.MonthBreakdownTooltip(null));
        Assert.Equal(
            "API換算の推定額（ローカルログから計算）",
            DailyCostText.MonthBreakdownTooltip(Array.Empty<ModelCostLine>()));
    }

    // N0 grouping follows the current culture; pin a known one (per-thread, so
    // it cannot leak into parallel tests).
    private static void WithJapaneseCulture(Action action)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Strings.Apply(japanese: true);
        }
    }
}
