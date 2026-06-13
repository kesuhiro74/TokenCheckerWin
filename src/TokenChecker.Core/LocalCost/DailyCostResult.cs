namespace TokenChecker.Core.LocalCost;

// Aggregated token counts and estimated cost for one provider's local session
// logs over a single day (or any UTC half-open interval). Contains only
// numeric totals — model ids and message/request ids are read transiently for
// pricing lookups and dedup, never returned — so it is always safe to display
// or serialize (privacy invariant).
public sealed record DailyCostResult(
    decimal CostUsd,
    long InputTokens,          // uncached input (billed at base input rate)
    long OutputTokens,
    long CacheWriteTokens,
    long CacheReadTokens,
    int EventCount,
    int UnknownModelEvents)
{
    public bool HasData => EventCount > 0;

    public static readonly DailyCostResult Empty = new(0m, 0, 0, 0, 0, 0, 0);
}
