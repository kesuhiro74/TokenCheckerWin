using System.Globalization;

namespace TokenChecker.App;

// One line of the per-model cost breakdown shown in the money row's tooltip:
// a display label (a normalized model name) plus its spend in both currencies.
internal sealed record ModelCostLine(string Label, DailyCost Cost);

// Pure shaping of DailyCostResult.CostByModelUsd into the few lines the tooltip
// shows: raw model ids are normalized to a display label (the trailing release
// date suffix is dropped), identical labels are merged, and the long tail is
// folded into a single "other" line so the tooltip stays readable.
//
// Privacy: model ids are inside the CLAUDE.md allowance (numbers and model
// names only); nothing else from the session logs reaches here.
internal static class CostBreakdown
{
    // How many individual models are listed before the rest is folded into the
    // "other" line.
    public const int MaxModelLines = 5;

    // Drops a trailing release-date suffix ("-20250514") so the dated and
    // undated ids of one model merge into a single display label. Anything else
    // is kept verbatim: these are vendor model names, not parsed identifiers.
    public static string NormalizeLabel(string? modelId)
    {
        var label = modelId?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            return Strings.T("モデル不明");
        }

        if (label.Length > 9 && label[^9] == '-' && IsAllDigits(label.AsSpan(label.Length - 8)))
        {
            label = label[..^9];
        }

        return label.Length == 0 ? Strings.T("モデル不明") : label;
    }

    // Normalized, merged and descending-sorted breakdown, capped at
    // MaxModelLines entries plus an aggregated "other" line. Zero/negative
    // costs are dropped (they add noise without telling the user anything).
    public static IReadOnlyList<ModelCostLine> Build(
        IReadOnlyDictionary<string, decimal>? costByModelUsd,
        decimal rateJpy)
    {
        if (costByModelUsd is null || costByModelUsd.Count == 0)
        {
            return Array.Empty<ModelCostLine>();
        }

        var merged = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (modelId, usd) in costByModelUsd)
        {
            if (usd <= 0m)
            {
                continue;
            }

            var label = NormalizeLabel(modelId);
            merged[label] = merged.TryGetValue(label, out var existing) ? existing + usd : usd;
        }

        if (merged.Count == 0)
        {
            return Array.Empty<ModelCostLine>();
        }

        // Label is the tie-breaker so the order is stable for equal amounts.
        var ordered = merged
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        var lines = new List<ModelCostLine>(Math.Min(ordered.Count, MaxModelLines) + 1);
        foreach (var entry in ordered.Take(MaxModelLines))
        {
            lines.Add(new ModelCostLine(entry.Key, ToCost(entry.Value, rateJpy)));
        }

        if (ordered.Count > MaxModelLines)
        {
            var rest = ordered.Skip(MaxModelLines).Sum(entry => entry.Value);
            lines.Add(new ModelCostLine(Strings.T("その他"), ToCost(rest, rateJpy)));
        }

        return lines;
    }

    private static DailyCost ToCost(decimal usd, decimal rateJpy) => new(usd, usd * rateJpy);

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    // Invariant-culture number text for the tooltip/extras rows, so the output
    // does not drift with the OS culture.
    internal static string FormatNumber(decimal value)
        => value == decimal.Truncate(value)
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("N2", CultureInfo.InvariantCulture);
}
