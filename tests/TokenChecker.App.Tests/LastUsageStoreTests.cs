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
        ResetCreditsNextExpiry: CapturedAt.AddDays(13));

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
