namespace TokenChecker.Core.Providers;

public sealed class CodexUsageProvider : IUsageProvider
{
    public string ServiceName => "Codex";

    public Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CommandLineProbe.ExistsOnPath("codex"))
        {
            return Task.FromResult(new ServiceUsage(
                ServiceName,
                ProviderStatus.NotInstalled,
                "Codex CLI was not found.",
                Array.Empty<RateLimitWindow>()));
        }

        return Task.FromResult(new ServiceUsage(
            ServiceName,
            ProviderStatus.NotLoggedIn,
            "Codex usage collection is not implemented yet.",
            Array.Empty<RateLimitWindow>()));
    }
}
