namespace TokenChecker.Core;

public sealed record RateLimitWindow(
    string Name,
    DateTimeOffset? ResetAtUtc,
    long? Used,
    long? Limit,
    long? Remaining);
