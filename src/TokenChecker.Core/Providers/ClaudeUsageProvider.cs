namespace TokenChecker.Core.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    public string ServiceName => "Claude";

    public Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CommandLineProbe.ExistsOnPath("claude"))
        {
            return Task.FromResult(new ServiceUsage(
                ServiceName,
                ProviderStatus.NotInstalled,
                "Claude CLI was not found.",
                Array.Empty<RateLimitWindow>()));
        }

        return Task.FromResult(new ServiceUsage(
            ServiceName,
            ProviderStatus.NotLoggedIn,
            "Claude usage collection is not implemented yet.",
            Array.Empty<RateLimitWindow>()));
    }
}
