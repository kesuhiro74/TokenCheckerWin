using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// Pure JSON parsing + classification helpers for the GitHub Copilot AI Credits
// provider, kept separate from the provider because the billing usage schema is
// volatile (usage-based billing / AI Credits rolled out 2026-06-01) and
// isolating the parsing makes it cheap to iterate.
//
// These types are public (not internal) on purpose: the POC runner lives in the
// separate TokenChecker.Poc assembly and reuses ParseUsageItems /
// IsCopilotAiCreditUsage to dump the real fields it observes.
//
// Money/credit math is done entirely in `decimal` (never materialized as
// `double`) to avoid binary floating-point drift on cent-scale values.
//
// Besides classification and the per-model split, this is also where the
// per-UTC-day grouping (GroupDailyCredits) that feeds the card's daily bar
// chart lives, plus the small Merge helper the provider uses to fold that
// grouping into the extras BuildExtras produced (BuildExtras itself stays pure
// and unaware of it).
public static class GitHubBillingUsageParser
{
    // Only the fields needed to classify a row and compute its credit value.
    // repositoryName is intentionally NOT captured so it can never leak through
    // the structured output or the item listing. grossAmount is captured (it was
    // not in the premium-request POC) so the "$ amount × 100 credits" confirming
    // fallback in §4.3 can be evaluated. netQuantity/netAmount are NOT used in
    // the consumption total (netQuantity was 0/null, netAmount is a
    // post-allowance/discount value); netAmount is instead the BILLED OVERAGE
    // reported as ServiceExtras.ExtraSpend. discountQuantity/discountAmount are
    // the part covered by the plan allowance, captured for observation only.
    //
    // Model / DiscountQuantity / DiscountAmount / Date are appended LAST with
    // defaults so every existing positional construction site keeps compiling.
    //
    // Date is the row's timestamp (GitHub reports one row per UTC day that had
    // usage, e.g. "2026-10-07T00:31:53Z"); null when absent or unparsable. It is
    // a date, not PII — it is what the per-day grouping needs.
    public sealed record GitHubBillingUsageItem(
        string? Product,
        string? Sku,
        string? UnitType,
        decimal? Quantity,
        decimal? GrossQuantity,
        decimal? GrossAmount,
        decimal? NetQuantity,
        decimal? NetAmount,
        string? Model = null,
        decimal? DiscountQuantity = null,
        decimal? DiscountAmount = null,
        DateTimeOffset? Date = null);

    // Extracts the authenticated user's login from a GET /user response.
    public static bool TryGetLogin(JsonNode? userRoot, out string login)
    {
        login = string.Empty;
        var value = GetString(userRoot?["login"]);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        login = value;
        return true;
    }

    // Parses the "usageItems" array of a billing usage response. Returns an empty
    // list for any unexpected shape. Callers that must tell "no usageItems array"
    // (unexpected/changed schema) apart from "empty array" (zero consumption)
    // should use TryParseUsageItems instead.
    public static IReadOnlyList<GitHubBillingUsageItem> ParseUsageItems(JsonNode? root)
        => TryParseUsageItems(root, out var items) ? items : Array.Empty<GitHubBillingUsageItem>();

    // Like ParseUsageItems but reports shape validity: returns false when
    // "usageItems" is absent or is not a JSON array (an unexpected/changed
    // schema), and true with a (possibly empty) list when it is a valid array.
    public static bool TryParseUsageItems(JsonNode? root, out IReadOnlyList<GitHubBillingUsageItem> items)
    {
        if (root?["usageItems"] is not JsonArray array)
        {
            items = Array.Empty<GitHubBillingUsageItem>();
            return false;
        }

        var parsed = new List<GitHubBillingUsageItem>(array.Count);
        foreach (var node in array)
        {
            if (node is null)
            {
                continue;
            }

            parsed.Add(new GitHubBillingUsageItem(
                GetString(node["product"]),
                GetString(node["sku"]),
                GetString(node["unitType"]),
                GetDecimal(node["quantity"]),
                GetDecimal(node["grossQuantity"]),
                GetDecimal(node["grossAmount"]),
                GetDecimal(node["netQuantity"]),
                GetDecimal(node["netAmount"]),
                GetString(node["model"]),
                GetDecimal(node["discountQuantity"]),
                GetDecimal(node["discountAmount"]),
                GetDate(node["date"])));
        }

        items = parsed;
        return true;
    }

    // Narrowed AI-credit classification (design §4.2). Premium-request rows
    // (unitType "requests" / sku "...premium request...") are excluded FIRST and
    // unconditionally, because Copilot also bills non-AI-credit lines. After that
    // a row counts only when it carries a confident AI-credit signal:
    //   strong (any one suffices): unitType / sku / product normalizes to contain
    //     "aicredit";
    //   weak (only with Copilot/AI-credit context): a generic "credits"/"credit"
    //     unitType, accepted only when product is Copilot or sku/product signals
    //     AI Credit.
    // This deliberately drops a bare generic-credits row that lacks any Copilot
    // marker rather than guess — the --raw observation finalizes the mapping.
    public static bool IsCopilotAiCreditUsage(GitHubBillingUsageItem item)
    {
        if (IsPremiumRequest(item))
        {
            return false;
        }

        if (UnitIsAiCredits(item.UnitType) || SkuIsAiCredit(item.Sku) || ProductIsAiCredit(item.Product))
        {
            return true;
        }

        return UnitIsGenericCredits(item.UnitType)
            && (ProductIsCopilot(item.Product) || SkuIsAiCredit(item.Sku) || ProductIsAiCredit(item.Product));
    }

    // Credits attributed to a single AI-credit row (design §4.3), all in decimal.
    // Primary: a credits/ai-credits unit's grossQuantity is the credit value
    // directly. Confirming fallback: $ grossAmount × 100 (1 credit = $0.01).
    // Last resort: grossQuantity ?? quantity ?? 0. Callers round the SUM once,
    // away-from-zero — never per row.
    public static decimal CreditsForRow(GitHubBillingUsageItem item)
    {
        if ((UnitIsAiCredits(item.UnitType) || UnitIsGenericCredits(item.UnitType)) && item.GrossQuantity is not null)
        {
            return item.GrossQuantity.Value;
        }

        if (item.GrossAmount is not null)
        {
            return item.GrossAmount.Value * 100m;
        }

        return item.GrossQuantity ?? item.Quantity ?? 0m;
    }

    // Builds the display-only extras for the Copilot card out of the rows that
    // IsCopilotAiCreditUsage already accepted: this month's per-model credit
    // split and the billed overage.
    //
    //  - Per-model credits: rows are grouped by their TRIMMED model name
    //    (Ordinal), summing CreditsForRow in decimal. Rows whose model is
    //    null/empty land in a single null-labelled entry (the App renders it as
    //    "unknown model"). When NO row carries a model at all — which is what
    //    the fallback /settings/billing/usage endpoint returns — no list is
    //    produced, so the card simply hides the breakdown rather than showing
    //    one meaningless "unknown model" row.
    //  - Extra spend: netAmount is the part NOT covered by the plan allowance
    //    (discountAmount is the covered part), i.e. the amount actually billed,
    //    in USD. Summed over the rows that report it; absent everywhere -> null.
    //    A total of 0 is a real answer ("no overage") and IS reported.
    //
    // Returns null when nothing worth showing came out, so the provider can
    // leave ServiceUsage.Extras null. Numbers and model display names only.
    public static ServiceExtras? BuildExtras(IEnumerable<GitHubBillingUsageItem>? copilotItems)
    {
        if (copilotItems is null)
        {
            return null;
        }

        // First-seen order is kept so the output is deterministic for equal
        // credit amounts; the App sorts it for display.
        var order = new List<string>();
        var byModel = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var unknownCredits = 0m;
        var hasUnknownModel = false;
        var hasAnyModel = false;
        var netTotal = 0m;
        var hasNetAmount = false;

        foreach (var item in copilotItems)
        {
            if (item is null)
            {
                continue;
            }

            var credits = CreditsForRow(item);
            var label = item.Model?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                hasUnknownModel = true;
                unknownCredits += credits;
            }
            else
            {
                hasAnyModel = true;
                if (byModel.TryGetValue(label, out var existing))
                {
                    byModel[label] = existing + credits;
                }
                else
                {
                    byModel[label] = credits;
                    order.Add(label);
                }
            }

            if (item.NetAmount is { } net)
            {
                hasNetAmount = true;
                netTotal += net;
            }
        }

        IReadOnlyList<ModelCredits>? models = null;
        if (hasAnyModel)
        {
            var list = new List<ModelCredits>(order.Count + 1);
            foreach (var label in order)
            {
                list.Add(new ModelCredits(label, byModel[label]));
            }

            if (hasUnknownModel)
            {
                list.Add(new ModelCredits(null, unknownCredits));
            }

            models = list;
        }

        var extras = new ServiceExtras(
            ExtraSpend: hasNetAmount ? new ExtraSpend(netTotal, null, "USD") : null,
            ModelCredits: models);

        return extras.IsEmpty ? null : extras;
    }

    // Groups already-classified Copilot AI-credit rows into one entry per UTC
    // CALENDAR DAY, ascending by date. Same input contract as BuildExtras: the
    // caller has already filtered with IsCopilotAiCreditUsage.
    //
    // The day key is the row's UTC day (DateOnly of Date.UtcDateTime), so a row
    // reported with a non-UTC offset still lands on the day GitHub bills it on.
    // GitHub's day boundary is 00:00 UTC — 09:00 JST — which is the SAME
    // boundary the card's "本日" delta uses.
    //
    // Rows without a usable date are skipped (they cannot be placed on a day);
    // when no row carried one at all the result is null, so the card simply
    // hides the chart instead of drawing an empty month. Credits are summed in
    // decimal; the display layer rounds.
    public static IReadOnlyList<DailyCredits>? GroupDailyCredits(IEnumerable<GitHubBillingUsageItem>? copilotItems)
    {
        if (copilotItems is null)
        {
            return null;
        }

        var byDay = new SortedDictionary<DateOnly, decimal>();
        foreach (var item in copilotItems)
        {
            if (item?.Date is not { } date)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(date.UtcDateTime);
            byDay[day] = byDay.TryGetValue(day, out var existing)
                ? existing + CreditsForRow(item)
                : CreditsForRow(item);
        }

        if (byDay.Count == 0)
        {
            return null;
        }

        var result = new List<DailyCredits>(byDay.Count);
        foreach (var pair in byDay)
        {
            result.Add(new DailyCredits(pair.Key, pair.Value));
        }

        return result;
    }

    // Folds a per-day grouping into the extras BuildExtras produced, keeping
    // BuildExtras itself pure and free of the second (optional) HTTP read.
    //  - no daily rows               -> the extras are returned untouched
    //    (still null when BuildExtras found nothing worth showing);
    //  - daily rows but no extras    -> a fresh extras carrying only them;
    //  - both                        -> the extras with DailyCredits attached.
    public static ServiceExtras? Merge(ServiceExtras? extras, IReadOnlyList<DailyCredits>? daily)
    {
        if (daily is null || daily.Count == 0)
        {
            return extras;
        }

        var merged = (extras ?? new ServiceExtras()) with { DailyCredits = daily };
        return merged.IsEmpty ? null : merged;
    }

    // Null-safe normalization (design §4.2): null/unset -> empty string, otherwise
    // lower-cased with all whitespace, '_' and '-' stripped so the many label
    // forms ("AI Credits" / "ai-credits" / "ai_credit") collapse to one token.
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-')
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static bool UnitIsAiCredits(string? unitType)
        => Normalize(unitType).Contains("aicredit", StringComparison.Ordinal);

    private static bool SkuIsAiCredit(string? sku)
        => Normalize(sku).Contains("aicredit", StringComparison.Ordinal);

    private static bool ProductIsAiCredit(string? product)
        => Normalize(product).Contains("aicredit", StringComparison.Ordinal);

    private static bool UnitIsGenericCredits(string? unitType)
    {
        var normalized = Normalize(unitType);
        return normalized is "credits" or "credit";
    }

    private static bool ProductIsCopilot(string? product)
        => Normalize(product).Contains("copilot", StringComparison.Ordinal);

    private static bool IsPremiumRequest(GitHubBillingUsageItem item)
        => Normalize(item.Sku).Contains("premiumrequest", StringComparison.Ordinal)
            || Normalize(item.UnitType) == "requests";

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();
    }

    // Reads a numeric/stringified-numeric node as decimal without ever passing
    // through double. A number node's ToString() yields its JSON literal (e.g.
    // "4626.9"), which decimal.TryParse consumes exactly; oversized values that
    // do not fit decimal simply return null (credit counts are small).
    private static decimal? GetDecimal(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        var text = node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    // Reads a row timestamp ("2026-10-07T00:31:53Z") with InvariantCulture.
    // TryParse (not ParseExact with "O") on purpose: the real values carry no
    // fractional seconds, which "O" would reject. AssumeUniversal makes a value
    // WITHOUT an offset count as UTC, which is how GitHub reports these days;
    // a value WITH an offset keeps it, and the caller takes .UtcDateTime.
    // Anything unparsable yields null and the row is skipped.
    private static DateTimeOffset? GetDate(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        var text = node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();

        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
