using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down provider isolation: one provider failing turns only its own service
// into Error, the others still capture, and genuine cancellation propagates.
public class UsageAggregatorTests
{
    private sealed class FakeProvider(string name, Func<CancellationToken, Task<ServiceUsage>> fn) : IUsageProvider
    {
        public string ServiceName { get; } = name;
        public Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default) => fn(cancellationToken);
    }

    private static ServiceUsage Ok(string name)
        => new(name, ProviderStatus.Available, null, new[] { new RateLimitWindow("w", null, 10, 100, 90, 10d, 300) });

    [Fact]
    public async Task AllSuccess_ReturnsAllAvailable_InProviderOrder()
    {
        var agg = new UsageAggregator(new IUsageProvider[]
        {
            new FakeProvider("Claude", _ => Task.FromResult(Ok("Claude"))),
            new FakeProvider("Codex", _ => Task.FromResult(Ok("Codex"))),
        });

        var snap = await agg.CaptureAsync();

        Assert.Equal(2, snap.Services.Count);
        Assert.Equal("Claude", snap.Services[0].ServiceName);
        Assert.Equal("Codex", snap.Services[1].ServiceName);
        Assert.All(snap.Services, s => Assert.Equal(ProviderStatus.Available, s.Status));
    }

    [Fact]
    public async Task OneProviderThrows_BecomesError_OthersUnaffected()
    {
        var agg = new UsageAggregator(new IUsageProvider[]
        {
            new FakeProvider("Claude", _ => Task.FromResult(Ok("Claude"))),
            new FakeProvider("Codex", _ => throw new InvalidOperationException("boom")),
        });

        var snap = await agg.CaptureAsync();

        Assert.Equal(ProviderStatus.Available, snap.Services[0].Status);
        var codex = snap.Services[1];
        Assert.Equal("Codex", codex.ServiceName);
        Assert.Equal(ProviderStatus.Error, codex.Status);
        Assert.Equal("Usage data could not be read.", codex.Message);
        Assert.Empty(codex.Windows);
    }

    [Fact]
    public async Task EmptyProviders_EmptySnapshot_WithTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var snap = await new UsageAggregator(Array.Empty<IUsageProvider>()).CaptureAsync();

        Assert.Empty(snap.Services);
        Assert.True(snap.CapturedAtUtc >= before);
    }

    [Fact]
    public async Task CancelledToken_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var agg = new UsageAggregator(new IUsageProvider[]
        {
            new FakeProvider("Claude", ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Ok("Claude"));
            }),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agg.CaptureAsync(cts.Token));
    }
}
