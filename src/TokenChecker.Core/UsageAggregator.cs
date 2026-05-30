namespace TokenChecker.Core;

public sealed class UsageAggregator
{
    private readonly IReadOnlyList<IUsageProvider> _providers;

    public UsageAggregator(IEnumerable<IUsageProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public async Task<UsageSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        // Capture every provider concurrently so a slow/timing-out provider
        // doesn't serialize behind the others. Claude (HTTP) and Codex (a
        // separate app-server process) are independent and share no mutable
        // state, so the total wait is max(provider) rather than the sum.
        var tasks = new Task<ServiceUsage>[_providers.Count];
        for (var i = 0; i < _providers.Count; i++)
        {
            tasks[i] = CaptureProviderAsync(_providers[i], cancellationToken);
        }

        // Task.WhenAll preserves the providers' order in the result array.
        var services = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new UsageSnapshot(DateTimeOffset.UtcNow, services);
    }

    // Per-provider isolation: a single provider failing must not fail the whole
    // capture. Real shutdown (the outer token being cancelled) still propagates
    // so the caller's "catch (OperationCanceledException) when (...)" path keeps
    // working.
    private static async Task<ServiceUsage> CaptureProviderAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ServiceUsage(
                provider.ServiceName,
                ProviderStatus.Error,
                "Usage data could not be read.",
                Array.Empty<RateLimitWindow>());
        }
    }
}
