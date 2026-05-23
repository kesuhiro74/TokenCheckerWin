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
        var services = new List<ServiceUsage>(_providers.Count);

        foreach (var provider in _providers)
        {
            try
            {
                services.Add(await provider.GetUsageAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                services.Add(new ServiceUsage(
                    provider.ServiceName,
                    ProviderStatus.Error,
                    "Usage data could not be read.",
                    Array.Empty<RateLimitWindow>()));
            }
        }

        return new UsageSnapshot(DateTimeOffset.UtcNow, services);
    }
}
