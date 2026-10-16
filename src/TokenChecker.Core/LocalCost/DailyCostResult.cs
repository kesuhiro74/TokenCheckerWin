namespace TokenChecker.Core.LocalCost;

// Aggregated token counts and estimated cost for one provider's local session
// logs over a single UTC half-open interval (a local day, a local calendar
// month, ...).
//
// Privacy: this type carries only numeric totals plus model ids. Model ids are
// within the allowance in CLAUDE.md ("numbers and model names only") and are
// needed for the per-model cost breakdown the UI shows. Message/request ids
// are read transiently for dedup and never returned; conversation content, cwd
// values and file paths are never captured at all. So a DailyCostResult is
// always safe to display.
public sealed record DailyCostResult(
    decimal CostUsd,
    long InputTokens,          // uncached input (billed at base input rate)
    long OutputTokens,
    long CacheWriteTokens,
    long CacheReadTokens,
    int EventCount,
    int UnknownModelEvents,
    // Model id -> USD cost over the interval. Only priced models appear here:
    // events whose model has no entry in ModelPricing are counted in
    // UnknownModelEvents and contribute no tokens or cost (existing policy),
    // so the values here always sum to CostUsd.
    IReadOnlyDictionary<string, decimal> CostByModelUsd,
    // Model id -> event count, for the models that contributed to
    // UnknownModelEvents (no ModelPricing entry matched). Diagnostic only: an
    // unpriced model contributes no tokens or cost, so this is purely "which
    // model ids need a ModelPricing entry". Model ids are within the
    // CLAUDE.md privacy allowance (numbers and model names only).
    IReadOnlyDictionary<string, int> UnknownModelEventsById)
{
    public bool HasData => EventCount > 0;

    // Declared before Empty so the static initializers run in the right order.
    public static readonly IReadOnlyDictionary<string, decimal> NoModelCosts =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, int> NoUnknownModels =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public static readonly DailyCostResult Empty = new(0m, 0, 0, 0, 0, 0, 0, NoModelCosts, NoUnknownModels);
}

// One UTC half-open interval [StartUtc, EndUtc) to aggregate over. Several can
// be passed to a reader's ComputeMany so that e.g. "today" and "this month"
// are produced by a single pass over the session logs.
public readonly record struct CostInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public bool Contains(DateTimeOffset timestamp)
        => timestamp >= StartUtc && timestamp < EndUtc;
}

// Mutable per-interval accumulator used while scanning. Internal to the
// readers; converted to an immutable DailyCostResult at the end.
internal sealed class DailyCostAccumulator
{
    private readonly Dictionary<string, decimal> _costByModel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknownModelEventsById = new(StringComparer.Ordinal);

    public decimal CostUsd { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public long CacheWriteTokens { get; private set; }
    public long CacheReadTokens { get; private set; }
    public int EventCount { get; private set; }
    public int UnknownModelEvents { get; private set; }

    public void AddEvent(
        string model,
        decimal costUsd,
        long inputTokens,
        long outputTokens,
        long cacheWriteTokens,
        long cacheReadTokens)
    {
        EventCount++;
        CostUsd += costUsd;
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        CacheWriteTokens += cacheWriteTokens;
        CacheReadTokens += cacheReadTokens;

        _costByModel[model] = _costByModel.TryGetValue(model, out var existing)
            ? existing + costUsd
            : costUsd;
    }

    // modelId may be null/blank for events with no model info at all (kept as
    // "" so it still shows up as "needs investigation" rather than vanishing).
    public void AddUnknownModelEvent(string? modelId)
    {
        UnknownModelEvents++;
        var key = string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim();
        _unknownModelEventsById[key] = _unknownModelEventsById.TryGetValue(key, out var existing)
            ? existing + 1
            : 1;
    }

    public DailyCostResult ToResult() => new(
        CostUsd,
        InputTokens,
        OutputTokens,
        CacheWriteTokens,
        CacheReadTokens,
        EventCount,
        UnknownModelEvents,
        _costByModel.Count == 0 ? DailyCostResult.NoModelCosts : _costByModel,
        _unknownModelEventsById.Count == 0 ? DailyCostResult.NoUnknownModels : _unknownModelEventsById);
}
