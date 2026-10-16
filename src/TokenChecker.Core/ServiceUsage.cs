namespace TokenChecker.Core;

// Extras is optional and defaults to null: every existing four-argument
// construction site keeps compiling, and a last_usage.json written before
// Extras existed still deserializes (the missing JSON property falls back to
// the constructor default).
public sealed record ServiceUsage(
    string ServiceName,
    ProviderStatus Status,
    string? Message,
    IReadOnlyList<RateLimitWindow> Windows,
    ServiceExtras? Extras = null);
