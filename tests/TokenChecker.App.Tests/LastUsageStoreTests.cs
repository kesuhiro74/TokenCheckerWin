using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// last_usage.json keeps the display-only Extras (plan label, per-model weekly
// limits, spend/credits) so a failed refresh still shows the plan badge and the
// extra-info row, while the provider diagnostic Message is still stripped.
public class LastUsageStoreTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snapshot(ServiceExtras? extras, string? message = "token=abc leaked")
        => new(
            CapturedAt,
            new[]
            {
                new ServiceUsage(
                    "Claude",
                    ProviderStatus.Available,
                    message,
                    new[]
                    {
                        new RateLimitWindow("five_hour", CapturedAt.AddHours(3), null, null, null, 38.0, 300)
                    },
                    extras)
            });

    private static ServiceExtras SampleExtras() => new(
        PlanLabel: "Max 5x",
        ScopedLimits: new[] { new ScopedLimit("Fable", 4.0, CapturedAt.AddDays(5)) },
        ExtraSpend: new ExtraSpend(12.34m, 50m, "USD"),
        CreditsBalance: 120m,
        CreditsUnlimited: false,
        ResetCreditsAvailable: 2,
        ResetCreditsNextExpiry: CapturedAt.AddDays(13),
        ModelCredits: new[]
        {
            new ModelCredits("gpt-5.6", 1234.5m),
            new ModelCredits(null, 10m)
        },
        DailyCredits: new[]
        {
            new DailyCredits(new DateOnly(2026, 10, 7), 1826.5m),
            new DailyCredits(new DateOnly(2026, 10, 8), 0m)
        },
        CreditAllowance: 20000m);

    [Fact]
    public void Sanitize_DropsMessage_ButKeepsExtras()
    {
        var sanitized = LastUsageStore.Sanitize(Snapshot(SampleExtras()));

        var service = Assert.Single(sanitized.Services);
        Assert.Null(service.Message);
        Assert.Equal("Max 5x", service.Extras?.PlanLabel);
        Assert.Equal(38.0, service.Windows[0].UsedPercent);
    }

    [Fact]
    public void RoundTrip_PreservesEveryExtrasMember()
    {
        var restored = LastUsageStore.FromJson(LastUsageStore.ToJson(Snapshot(SampleExtras())));

        Assert.NotNull(restored);
        var service = Assert.Single(restored!.Services);
        Assert.Null(service.Message);

        var extras = service.Extras;
        Assert.NotNull(extras);
        Assert.Equal("Max 5x", extras!.PlanLabel);
        Assert.Equal(12.34m, extras.ExtraSpend?.Used);
        Assert.Equal(50m, extras.ExtraSpend?.Limit);
        Assert.Equal("USD", extras.ExtraSpend?.Unit);
        Assert.Equal(120m, extras.CreditsBalance);
        Assert.False(extras.CreditsUnlimited);
        Assert.Equal(2, extras.ResetCreditsAvailable);
        Assert.Equal(CapturedAt.AddDays(13), extras.ResetCreditsNextExpiry);

        var scoped = Assert.Single(extras.ScopedLimits!);
        Assert.Equal("Fable", scoped.Label);
        Assert.Equal(4.0, scoped.UsedPercent);
        Assert.Equal(CapturedAt.AddDays(5), scoped.ResetAtUtc);

        // The Copilot per-model credit split survives too (display names and
        // numbers only), including the null-labelled "unknown model" bucket.
        var models = extras.ModelCredits!;
        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-5.6", models[0].Label);
        Assert.Equal(1234.5m, models[0].Credits);
        Assert.Null(models[1].Label);
        Assert.Equal(10m, models[1].Credits);

        // So does the Copilot per-UTC-day consumption behind the daily chart.
        // DateOnly is round-tripped by System.Text.Json itself (no converter of
        // ours), so this pins that the stored "2026-10-07" form comes back.
        var daily = extras.DailyCredits!;
        Assert.Equal(2, daily.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), daily[0].UtcDate);
        Assert.Equal(1826.5m, daily[0].Credits);
        Assert.Equal(new DateOnly(2026, 10, 8), daily[1].UtcDate);
        Assert.Equal(0m, daily[1].Credits);

        // And the Copilot monthly credit allowance auto-detected from GitHub (a
        // number only), so a failed refresh still shows the cap and remaining.
        Assert.Equal(20000m, extras.CreditAllowance);
    }

    [Fact]
    public void ToJson_WritesDailyCreditsAsPlainDates()
        => Assert.Contains("\"2026-10-07\"", LastUsageStore.ToJson(Snapshot(SampleExtras())), StringComparison.Ordinal);

    [Fact]
    public void RoundTrip_LegacyExtrasWithoutModelOrDailyCredits_StillLoads()
    {
        // Exactly what a build before the Copilot per-model breakdown and the
        // daily chart wrote.
        const string legacy = """
        {
          "CapturedAtUtc": "2026-10-16T09:00:00+00:00",
          "Services": [
            {
              "ServiceName": "GitHub Copilot",
              "Status": 1,
              "Message": null,
              "Windows": [],
              "Extras": { "PlanLabel": "Copilot Pro+", "CreditsUnlimited": false }
            }
          ]
        }
        """;

        var restored = LastUsageStore.FromJson(legacy);

        var extras = Assert.Single(restored!.Services).Extras;
        Assert.Equal("Copilot Pro+", extras?.PlanLabel);
        Assert.Null(extras?.ModelCredits);
        Assert.Null(extras?.DailyCredits);
        Assert.Null(extras?.CreditAllowance);
    }

    [Fact]
    public void RoundTrip_NoExtras_StaysNull()
    {
        var restored = LastUsageStore.FromJson(LastUsageStore.ToJson(Snapshot(extras: null)));
        Assert.Null(Assert.Single(restored!.Services).Extras);
    }

    [Fact]
    public void SaveThenLoad_PersistsExtrasAndStripsMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tokenchecker-last-usage-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LastUsageStore(path);
            store.Save(Snapshot(SampleExtras()));

            var loaded = store.Load();
            Assert.NotNull(loaded);
            var service = Assert.Single(loaded!.Services);
            Assert.Null(service.Message);
            Assert.Equal("Max 5x", service.Extras?.PlanLabel);

            // The persisted file must not carry the diagnostic text.
            Assert.DoesNotContain("token=abc", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
        => Assert.Null(new LastUsageStore(
            Path.Combine(Path.GetTempPath(), $"tokenchecker-missing-{Guid.NewGuid():N}.json")).Load());
}
