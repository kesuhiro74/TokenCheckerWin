using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace TokenChecker.Core.Tests;

// ServiceUsage.Extras was added after last_usage.json shipped, so a snapshot
// persisted by an older build must still load (Extras simply absent). This
// mirrors what LastUsageStore does: JsonSerializer over UsageSnapshot with
// WriteIndented, no custom converters.
public sealed class ServiceUsageExtrasSerializationTests
{
    private static readonly JsonSerializerOptions StoreOptions = new() { WriteIndented = true };

    // Exactly the shape a pre-Extras build wrote.
    private const string LegacySnapshotJson = """
    {
      "CapturedAtUtc": "2026-10-16T03:00:00+00:00",
      "Services": [
        {
          "ServiceName": "Claude",
          "Status": 1,
          "Message": null,
          "Windows": [
            {
              "Name": "Claude 5h",
              "ResetAtUtc": "2026-10-16T12:00:00+00:00",
              "Used": 37,
              "Limit": 100,
              "Remaining": 63,
              "UsedPercent": 37,
              "WindowDurationMins": 300
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void LegacySnapshotWithoutExtras_StillDeserializes()
    {
        var snapshot = JsonSerializer.Deserialize<UsageSnapshot>(LegacySnapshotJson, StoreOptions);

        Assert.NotNull(snapshot);
        var service = Assert.Single(snapshot!.Services);
        Assert.Equal("Claude", service.ServiceName);
        Assert.Equal(ProviderStatus.Available, service.Status);
        Assert.Null(service.Extras);
        Assert.Equal(37d, Assert.Single(service.Windows).UsedPercent);
    }

    [Fact]
    public void ExtrasRoundTrip()
    {
        var original = new UsageSnapshot(
            new DateTimeOffset(2026, 10, 16, 3, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ServiceUsage(
                    "Claude",
                    ProviderStatus.Available,
                    null,
                    Array.Empty<RateLimitWindow>(),
                    new ServiceExtras(
                        PlanLabel: "Max 5x",
                        ScopedLimits: new[]
                        {
                            new ScopedLimit("Fable", 4d, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero))
                        },
                        ExtraSpend: new ExtraSpend(12.34m, 50m, "USD"),
                        CreditsBalance: 12.5m,
                        CreditsUnlimited: true,
                        ResetCreditsAvailable: 2,
                        ResetCreditsNextExpiry: DateTimeOffset.FromUnixTimeSeconds(1760500000)))
            });

        var json = JsonSerializer.Serialize(original, StoreOptions);
        var restored = JsonSerializer.Deserialize<UsageSnapshot>(json, StoreOptions);

        var extras = Assert.Single(restored!.Services).Extras!;
        Assert.Equal("Max 5x", extras.PlanLabel);
        Assert.Equal("Fable", Assert.Single(extras.ScopedLimits!).Label);
        Assert.Equal(12.34m, extras.ExtraSpend!.Used);
        Assert.Equal(50m, extras.ExtraSpend.Limit);
        Assert.Equal("USD", extras.ExtraSpend.Unit);
        Assert.Equal(12.5m, extras.CreditsBalance);
        Assert.True(extras.CreditsUnlimited);
        Assert.Equal(2, extras.ResetCreditsAvailable);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760500000), extras.ResetCreditsNextExpiry);
        Assert.Null(extras.CreditAllowance);
    }

    [Fact]
    public void ServiceUsage_WithoutExtras_StaysConstructibleWithFourArguments()
    {
        // Every pre-existing construction site passes four arguments.
        var usage = new ServiceUsage("Codex", ProviderStatus.Error, "msg", Array.Empty<RateLimitWindow>());

        Assert.Null(usage.Extras);
    }

    [Fact]
    public void DerivedIsEmpty_IsNotSerialized()
    {
        // It is a computed helper, not data: it must not end up in
        // last_usage.json or the POC output.
        var json = JsonSerializer.Serialize(new ServiceExtras(PlanLabel: "Pro"), StoreOptions);

        Assert.DoesNotContain("IsEmpty", json, StringComparison.Ordinal);
        Assert.Contains("\"PlanLabel\": \"Pro\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyExtras_IsReportedAsEmpty()
    {
        Assert.True(new ServiceExtras().IsEmpty);
        Assert.True(new ServiceExtras(ScopedLimits: Array.Empty<ScopedLimit>()).IsEmpty);
        Assert.True(new ServiceExtras(ModelCredits: Array.Empty<ModelCredits>()).IsEmpty);
        Assert.True(new ServiceExtras(DailyCredits: Array.Empty<DailyCredits>()).IsEmpty);
        Assert.False(new ServiceExtras(PlanLabel: "Pro").IsEmpty);
        Assert.False(new ServiceExtras(ResetCreditsAvailable: 0).IsEmpty);
        Assert.False(new ServiceExtras(ModelCredits: new[] { new ModelCredits("gpt-5.6", 10m) }).IsEmpty);
        Assert.False(
            new ServiceExtras(DailyCredits: new[] { new DailyCredits(new DateOnly(2026, 10, 7), 1826m) }).IsEmpty);
        // The Copilot allowance auto-detected from GitHub is on its own enough
        // to be worth persisting (it is what the card's cap/remaining needs).
        Assert.False(new ServiceExtras(CreditAllowance: 20000m).IsEmpty);
    }

    [Fact]
    public void DailyCreditsRoundTrip_KeepsTheDateOnlyAndTheDecimal()
    {
        // DateOnly is serialized by System.Text.Json as "yyyy-MM-dd" with no
        // converter of our own — this pins that it survives last_usage.json.
        var original = new ServiceExtras(
            DailyCredits: new[]
            {
                new DailyCredits(new DateOnly(2026, 10, 7), 1826.5m),
                new DailyCredits(new DateOnly(2026, 10, 8), 0m)
            });

        var json = JsonSerializer.Serialize(original, StoreOptions);
        Assert.Contains("2026-10-07", json, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<ServiceExtras>(json, StoreOptions)!;
        Assert.Equal(2, restored.DailyCredits!.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), restored.DailyCredits[0].UtcDate);
        Assert.Equal(1826.5m, restored.DailyCredits[0].Credits);
        Assert.Equal(new DateOnly(2026, 10, 8), restored.DailyCredits[1].UtcDate);
        Assert.Equal(0m, restored.DailyCredits[1].Credits);
    }

    [Fact]
    public void ExtrasAreOmitted_WhenNullIgnoringIsEnabled()
    {
        // The POC serializes snapshots with WhenWritingNull, so a service
        // without extras must not grow an "Extras": null line.
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(
            new ServiceUsage("Codex", ProviderStatus.Available, null, Array.Empty<RateLimitWindow>()),
            options);

        Assert.DoesNotContain("Extras", json, StringComparison.Ordinal);
    }
}
