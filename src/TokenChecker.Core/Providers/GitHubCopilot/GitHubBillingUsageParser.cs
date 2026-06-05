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
public static class GitHubBillingUsageParser
{
    // Only the fields needed to classify a row and compute its credit value.
    // repositoryName is intentionally NOT captured so it can never leak through
    // the structured output or the item listing. grossAmount is captured (it was
    // not in the premium-request POC) so the "$ amount × 100 credits" confirming
    // fallback in §4.3 can be evaluated. netQuantity/netAmount are kept only for
    // raw schema observation; they are NOT used in the consumption total
    // (netQuantity was 0/null, netAmount is a post-allowance/discount value).
    public sealed record GitHubBillingUsageItem(
        string? Product,
        string? Sku,
        string? UnitType,
        decimal? Quantity,
        decimal? GrossQuantity,
        decimal? GrossAmount,
        decimal? NetQuantity,
        decimal? NetAmount);

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
                GetDecimal(node["netAmount"])));
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
}
