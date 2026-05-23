namespace TokenChecker.Core;

public interface IUsageProvider
{
    string ServiceName { get; }

    Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default);
}
