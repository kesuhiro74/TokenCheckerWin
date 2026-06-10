using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Pins the tray icon's 80/95 escalation (TrayIconRenderer.DetermineState) — the
// third of CLAUDE.md's "must stay identical" threshold sites. The other two
// (StatusForm.UsageAccentColor / BrandUsageColor) delegate to UsageTheme, which
// UsageThemeTests already covers; DetermineState had its own inline comparison and
// was previously untested.
public class DetermineStateTests
{
    // `expected` is the OverallState name (not the enum value): the enum lives on
    // the internal TrayIconRenderer, and a public xUnit signature cannot expose a
    // less-accessible type. Comparing names keeps the band assertion readable.
    // Boundaries reference UsageTheme.WarningPercent/CriticalPercent (the single
    // source of truth) rather than literal 80/95, so this test tracks the canonical
    // constants instead of pinning an independent copy of the numbers.
    [Theory]
    [InlineData(0d, "Normal")]
    [InlineData(UsageTheme.WarningPercent - 0.1, "Normal")]
    [InlineData(UsageTheme.WarningPercent, "Warning")]
    [InlineData(UsageTheme.CriticalPercent - 0.1, "Warning")]
    [InlineData(UsageTheme.CriticalPercent, "Danger")]
    [InlineData(100d, "Danger")]
    public void EscalatesAt80And95(double percent, string expected)
    {
        var state = TrayIconRenderer.DetermineState(SnapshotWith(percent, null), out var claudePercent, out _);
        Assert.Equal(expected, state.ToString());
        Assert.Equal(percent, claudePercent);
    }

    [Fact]
    public void BothErrorLike_IsError()
    {
        var snapshot = SnapshotWith(null, null, ProviderStatus.Unauthorized, ProviderStatus.Error);
        Assert.Equal(TrayIconRenderer.OverallState.Error, TrayIconRenderer.DetermineState(snapshot, out _, out _));
    }

    [Fact]
    public void NoData_IsUnknown()
    {
        var snapshot = new UsageSnapshot(DateTimeOffset.UnixEpoch, Array.Empty<ServiceUsage>());
        Assert.Equal(TrayIconRenderer.OverallState.Unknown, TrayIconRenderer.DetermineState(snapshot, out _, out _));
    }

    [Fact]
    public void HighestPercentWins_AcrossServices()
    {
        // Claude 50 (Normal) + Codex 96 (Danger) -> Danger from the max.
        var snapshot = SnapshotWith(50d, 96d);
        Assert.Equal(TrayIconRenderer.OverallState.Danger, TrayIconRenderer.DetermineState(snapshot, out _, out _));
    }

    private static UsageSnapshot SnapshotWith(
        double? claudePercent,
        double? codexPercent,
        ProviderStatus claudeStatus = ProviderStatus.Available,
        ProviderStatus codexStatus = ProviderStatus.Available)
    {
        var services = new List<ServiceUsage>();
        if (claudePercent is not null || claudeStatus != ProviderStatus.Available)
        {
            services.Add(Service("Claude", claudeStatus, claudePercent));
        }

        if (codexPercent is not null || codexStatus != ProviderStatus.Available)
        {
            services.Add(Service("Codex", codexStatus, codexPercent));
        }

        return new UsageSnapshot(DateTimeOffset.UnixEpoch, services);
    }

    private static ServiceUsage Service(string name, ProviderStatus status, double? percent)
    {
        var windows = percent is null
            ? Array.Empty<RateLimitWindow>()
            : new[] { new RateLimitWindow($"{name} 5h", null, null, 100, null, percent, 300) };
        return new ServiceUsage(name, status, null, windows);
    }
}
