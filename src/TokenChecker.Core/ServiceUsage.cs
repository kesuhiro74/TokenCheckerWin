namespace TokenChecker.Core;

public sealed record ServiceUsage(
    string ServiceName,
    ProviderStatus Status,
    string? Message,
    IReadOnlyList<RateLimitWindow> Windows);
