using System.Globalization;
using TokenChecker.Core;

namespace TokenChecker.App;

// One row of the Copilot card's "this month by model" block: a display label,
// the credits attributed to it, and its share of the block's total.
internal sealed record CopilotModelLine(string Label, decimal Credits, double SharePercent);

// Pure shaping of ServiceExtras.ModelCredits into the few rows the Copilot card
// shows. Mirrors CostBreakdown (same cap, same "other" fold, same ordering and
// tie-break), but this one is credit-based, not money-based, and adds the share
// percentage — so it is a sibling of CostBreakdown rather than a reuse of it.
//
// Privacy: model display names and numbers only — see the note on ServiceExtras.
internal static class CopilotModelBreakdown
{
    // How many individual models are listed before the rest is folded into the
    // "other" row. The card therefore shows at most MaxModelLines + 1 rows.
    public const int MaxModelLines = 5;

    // Merged, descending-sorted breakdown capped at MaxModelLines rows plus an
    // aggregated "other" row. Zero/negative credits are dropped (they tell the
    // user nothing), a null/blank model label becomes "unknown model", and equal
    // labels are summed. The share is each row's percentage of the TOTAL OF THE
    // ROWS SHOWN; rounded shares may not add up to exactly 100.
    public static IReadOnlyList<CopilotModelLine> Build(IReadOnlyList<ModelCredits>? credits)
    {
        if (credits is null || credits.Count == 0)
        {
            return Array.Empty<CopilotModelLine>();
        }

        var merged = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var entry in credits)
        {
            if (entry is null || entry.Credits <= 0m)
            {
                continue;
            }

            var label = NormalizeLabel(entry.Label);
            merged[label] = merged.TryGetValue(label, out var existing) ? existing + entry.Credits : entry.Credits;
        }

        if (merged.Count == 0)
        {
            return Array.Empty<CopilotModelLine>();
        }

        // Label is the tie-breaker so the order is stable for equal amounts.
        var ordered = merged
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        var total = ordered.Sum(entry => entry.Value);
        var lines = new List<CopilotModelLine>(Math.Min(ordered.Count, MaxModelLines) + 1);
        foreach (var entry in ordered.Take(MaxModelLines))
        {
            lines.Add(new CopilotModelLine(entry.Key, entry.Value, Share(entry.Value, total)));
        }

        if (ordered.Count > MaxModelLines)
        {
            var rest = ordered.Skip(MaxModelLines).Sum(entry => entry.Value);
            lines.Add(new CopilotModelLine(Strings.T("その他"), rest, Share(rest, total)));
        }

        return lines;
    }

    // The right-hand value of a row: "1,234（26%）". The credits are rounded to
    // whole credits (away from zero) so the rows speak the same unit as the
    // card's hero number, which is also a whole-credit figure. A non-zero share
    // that would round to 0 reads "<1" so a model that was used never shows 0%.
    public static string ValueText(CopilotModelLine line)
        => Strings.Tf(
            "{0}（{1}%）",
            Math.Round(line.Credits, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture),
            ShareText(line.SharePercent));

    private static string ShareText(double sharePercent)
    {
        var rounded = Math.Round(sharePercent, MidpointRounding.AwayFromZero);
        return rounded == 0d && sharePercent > 0d
            ? "<1"
            : rounded.ToString("0", CultureInfo.InvariantCulture);
    }

    // A null/blank model (the fallback billing endpoint omits the field on some
    // rows) is still worth showing as a labelled bucket.
    private static string NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? Strings.T("モデル不明") : trimmed;
    }

    private static double Share(decimal value, decimal total)
        => total <= 0m ? 0d : (double)(value / total * 100m);
}
