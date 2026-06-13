namespace TokenChecker.Core.LocalCost;

// Per-model price entry in USD per million tokens. IdPrefix is matched against
// the model id reported in local session logs (longest prefix wins).
public sealed record ModelPrice(
    string IdPrefix,
    decimal InputUsdPerMTok,
    decimal OutputUsdPerMTok,
    decimal CacheWriteUsdPerMTok,
    decimal CacheReadUsdPerMTok);

// Static USD-per-MTok pricing table for the models that show up in local
// Claude Code / Codex session logs, plus the cost formula.
//
// This table is the single source of truth for unit prices (values confirmed
// against the official pricing pages as of 2026-06; verify against official
// pricing pages when updating). All math is done in `decimal` — never float or
// double — to avoid binary floating-point drift on sub-cent values (same
// convention as GitHubBillingUsageParser).
public static class ModelPricing
{
    // Ordered by descending prefix length at lookup time so the longest prefix
    // wins (e.g. "gpt-5.4-mini" must beat "gpt-5.4", which must beat "gpt-5").
    private static readonly ModelPrice[] Prices =
    {
        // Anthropic (input / output / cache write / cache read, USD per MTok).
        new("claude-fable-5", 10m, 50m, 12.5m, 1m),
        new("claude-mythos", 10m, 50m, 12.5m, 1m),
        new("claude-opus-4-5", 5m, 25m, 6.25m, 0.5m),
        new("claude-opus-4-6", 5m, 25m, 6.25m, 0.5m),
        new("claude-opus-4-7", 5m, 25m, 6.25m, 0.5m),
        new("claude-opus-4-8", 5m, 25m, 6.25m, 0.5m),
        new("claude-opus-4-0", 15m, 75m, 18.75m, 1.5m),
        new("claude-opus-4-1", 15m, 75m, 18.75m, 1.5m),
        // "claude-opus-4-2" exists to catch the legacy full id
        // claude-opus-4-20250514 via prefix matching.
        new("claude-opus-4-2", 15m, 75m, 18.75m, 1.5m),
        new("claude-sonnet-4", 3m, 15m, 3.75m, 0.3m),
        new("claude-haiku-4", 1m, 5m, 1.25m, 0.1m),
        new("claude-3-5-haiku", 0.8m, 4m, 1m, 0.08m),

        // OpenAI (cache write is free, so 0).
        new("gpt-5.5", 5m, 30m, 0m, 0.5m),
        new("gpt-5.4-mini", 0.75m, 4.5m, 0m, 0.075m),
        new("gpt-5.4-nano", 0.2m, 1.25m, 0m, 0.02m),
        new("gpt-5.4", 2.5m, 15m, 0m, 0.25m),
        // Includes gpt-5.3-codex.
        new("gpt-5.3", 1.75m, 14m, 0m, 0.175m),
        new("gpt-5-mini", 0.25m, 2m, 0m, 0.025m),
        new("gpt-5-nano", 0.05m, 0.4m, 0m, 0.005m),
        // Fallback for gpt-5 / gpt-5-codex / gpt-5.1 / gpt-5.1-codex etc.
        new("gpt-5", 1.25m, 10m, 0m, 0.125m)
    };

    // Same table sorted once so lookups scan longest prefixes first.
    private static readonly ModelPrice[] PricesByPrefixLengthDesc =
        Prices.OrderByDescending(p => p.IdPrefix.Length).ToArray();

    // Resolves a model id to its price entry via longest prefix match.
    // Returns null for null/empty/unknown ids (including "<synthetic>" rows
    // Claude Code emits for non-billable internal events).
    public static ModelPrice? Find(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var normalized = modelId.Trim().ToLowerInvariant();
        foreach (var price in PricesByPrefixLengthDesc)
        {
            if (normalized.StartsWith(price.IdPrefix, StringComparison.Ordinal))
            {
                return price;
            }
        }

        return null;
    }

    public static decimal Cost(
        ModelPrice price,
        long inputTokens,
        long outputTokens,
        long cacheWriteTokens,
        long cacheReadTokens)
        => (inputTokens * price.InputUsdPerMTok
            + outputTokens * price.OutputUsdPerMTok
            + cacheWriteTokens * price.CacheWriteUsdPerMTok
            + cacheReadTokens * price.CacheReadUsdPerMTok)
            / 1_000_000m;
}
