using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the provider-extras rows of the status card: the per-model weekly
// limit row (text + the percentage that drives its severity color) and the
// extra-info row (money/credit spend, Codex reset credits, credit balance), in
// both languages — an English assertion also proves the Strings key is
// registered (a missing key would echo the Japanese template).
[Collection("StringsLanguage")]
public class ServiceExtrasTextTests
{
    private static ServiceExtras Extras(
        string? plan = null,
        IReadOnlyList<ScopedLimit>? scoped = null,
        ExtraSpend? spend = null,
        decimal? credits = null,
        bool unlimited = false,
        int? resetCredits = null,
        DateTimeOffset? resetExpiry = null)
        => new(plan, scoped, spend, credits, unlimited, resetCredits, resetExpiry);

    [Fact]
    public void ScopedLimits_Japanese_JoinsEntriesAndRoundsPercent()
    {
        Strings.Apply(japanese: true);
        var text = ServiceExtrasText.ScopedLimits(Extras(scoped: new[]
        {
            new ScopedLimit("Fable", 4.4, null),
            new ScopedLimit("Sonnet", 12.6, null)
        }));

        Assert.Equal($"Fable 週 4%{ServiceExtrasText.ItemSeparator}Sonnet 週 13%", text);
    }

    [Fact]
    public void ScopedLimits_English_UsesTranslatedTemplate()
    {
        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal(
                "Fable weekly 4%",
                ServiceExtrasText.ScopedLimits(Extras(scoped: new[] { new ScopedLimit("Fable", 4.0, null) })));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void ScopedLimits_WithoutPercent_IsSkipped()
    {
        Strings.Apply(japanese: true);
        var scoped = new[]
        {
            new ScopedLimit("NoValue", null, null),
            new ScopedLimit("Fable", 7.0, null)
        };

        Assert.Equal("Fable 週 7%", ServiceExtrasText.ScopedLimits(Extras(scoped: scoped)));
    }

    [Fact]
    public void ScopedLimits_None_IsEmpty()
    {
        Assert.Equal(string.Empty, ServiceExtrasText.ScopedLimits(null));
        Assert.Equal(string.Empty, ServiceExtrasText.ScopedLimits(Extras()));
        Assert.Equal(string.Empty, ServiceExtrasText.ScopedLimits(Extras(scoped: Array.Empty<ScopedLimit>())));
    }

    [Fact]
    public void MaxScopedPercent_ReturnsHighestOrNull()
    {
        Assert.Null(ServiceExtrasText.MaxScopedPercent(null));
        Assert.Null(ServiceExtrasText.MaxScopedPercent(Extras(scoped: new[] { new ScopedLimit("a", null, null) })));
        Assert.Equal(
            91.0,
            ServiceExtrasText.MaxScopedPercent(Extras(scoped: new[]
            {
                new ScopedLimit("a", 12.0, null),
                new ScopedLimit("b", 91.0, null)
            })));
    }

    [Fact]
    public void Info_MoneySpendWithLimit_UsesCurrencySymbol()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(
            "追加利用 $12.34 / $50.00",
            ServiceExtrasText.Info(Extras(spend: new ExtraSpend(12.34m, 50m, "USD"))));
    }

    [Fact]
    public void Info_MoneySpendWithoutLimit_OmitsTheCap()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(
            "追加利用 $12.34",
            ServiceExtrasText.Info(Extras(spend: new ExtraSpend(12.34m, null, "USD"))));
    }

    [Fact]
    public void Info_UnknownCurrencyCode_FallsBackToCodeSuffix()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(
            "追加利用 12.34 CHF",
            ServiceExtrasText.Info(Extras(spend: new ExtraSpend(12.34m, null, "chf"))));
    }

    [Fact]
    public void Info_CreditsSpend_ReadsAsAMonthlyCap()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(
            "月次上限 30 / 100 クレジット",
            ServiceExtrasText.Info(Extras(spend: new ExtraSpend(30m, 100m, "credits"))));
        Assert.Equal(
            "追加利用 30 クレジット",
            ServiceExtrasText.Info(Extras(spend: new ExtraSpend(30m, null, "credits"))));
    }

    [Fact]
    public void Info_ResetCredits_ShowsCountAndLocalExpiryDate()
    {
        Strings.Apply(japanese: true);
        var expiryUtc = new DateTimeOffset(2026, 10, 29, 16, 47, 58, TimeSpan.Zero);
        var expectedDate = expiryUtc.ToLocalTime().ToString("M/d");

        Assert.Equal(
            $"リセット券 2枚（{expectedDate} まで）",
            ServiceExtrasText.Info(Extras(resetCredits: 2, resetExpiry: expiryUtc)));
        Assert.Equal(
            "リセット券 2枚",
            ServiceExtrasText.Info(Extras(resetCredits: 2)));
    }

    [Fact]
    public void Info_ResetCreditsZero_IsOmitted()
    {
        Strings.Apply(japanese: true);
        Assert.Equal(string.Empty, ServiceExtrasText.Info(Extras(resetCredits: 0)));
    }

    [Fact]
    public void Info_Credits_UnlimitedWinsOverBalance()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("クレジット 無制限", ServiceExtrasText.Info(Extras(unlimited: true, credits: 5m)));
        Assert.Equal("クレジット 120", ServiceExtrasText.Info(Extras(credits: 120m)));
        Assert.Equal("クレジット 0.50", ServiceExtrasText.Info(Extras(credits: 0.5m)));
    }

    [Fact]
    public void Info_English_UsesTranslatedTemplates()
    {
        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal(
                "Extra spend $12.34 / $50.00",
                ServiceExtrasText.Info(Extras(spend: new ExtraSpend(12.34m, 50m, "USD"))));
            Assert.Equal(
                "Monthly cap 30 / 100 credits",
                ServiceExtrasText.Info(Extras(spend: new ExtraSpend(30m, 100m, "credits"))));
            Assert.Equal("2 reset credits", ServiceExtrasText.Info(Extras(resetCredits: 2)));
            Assert.Equal("Credits unlimited", ServiceExtrasText.Info(Extras(unlimited: true)));
            Assert.Equal("Credits 120", ServiceExtrasText.Info(Extras(credits: 120m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Info_SeveralItems_AreJoinedInOrder()
    {
        Strings.Apply(japanese: true);
        var text = ServiceExtrasText.Info(Extras(
            spend: new ExtraSpend(1m, null, "USD"),
            resetCredits: 2,
            unlimited: true));

        Assert.Equal(
            string.Join(ServiceExtrasText.ItemSeparator, "追加利用 $1.00", "リセット券 2枚", "クレジット 無制限"),
            text);
    }

    [Fact]
    public void Info_NothingReported_IsEmpty()
    {
        Assert.Equal(string.Empty, ServiceExtrasText.Info(null));
        Assert.Equal(string.Empty, ServiceExtrasText.Info(Extras(plan: "Max 5x")));
    }
}
