using System.Globalization;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// Pure JSON parsing helpers for the GitHub Copilot POC, kept separate from the
// provider because the billing usage schema is volatile (usage-based billing
// rolls out 2026-06-01) and isolating the parsing makes it cheap to iterate.
//
// These types are public (not internal) on purpose: the POC runner lives in the
// separate TokenChecker.Poc assembly and reuses ParseUsageItems/
// IsCopilotPremiumRequestUsage to dump the real fields it observes. This is
// experimental surface only.
public static class GitHubBillingUsageParser
{
    // Only the fields the POC needs to observe for the final mapping decision.
    // repositoryName is intentionally NOT captured so it can never leak through
    // the structured output or the item listing. Both quantity and grossQuantity
    // are captured because netQuantity was observed as 0/null and is unusable for
    // the consumption total (see docs/experiments/github-copilot/findings.md).
    public sealed record GitHubBillingUsageItem(
        string? Product,
        string? Sku,
        string? UnitType,
        double? Quantity,
        double? GrossQuantity,
        double? NetQuantity,
        double? NetAmount);

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

    // Parses the "usageItems" array of a billing usage / premium_request usage
    // response. Returns an empty list for any unexpected shape (defensive: the
    // schema may shift on 2026-06-01).
    public static IReadOnlyList<GitHubBillingUsageItem> ParseUsageItems(JsonNode? root)
    {
        if (root?["usageItems"] is not JsonArray items)
        {
            return Array.Empty<GitHubBillingUsageItem>();
        }

        var parsed = new List<GitHubBillingUsageItem>(items.Count);
        foreach (var node in items)
        {
            if (node is null)
            {
                continue;
            }

            parsed.Add(new GitHubBillingUsageItem(
                GetString(node["product"]),
                GetString(node["sku"]),
                GetString(node["unitType"]),
                GetDouble(node["quantity"]),
                GetDouble(node["grossQuantity"]),
                GetDouble(node["netQuantity"]),
                GetDouble(node["netAmount"])));
        }

        return parsed;
    }

    // Strict Copilot Premium Request match, finalized after observing the real
    // --raw fields (2026-05-31): product "copilot"/"Copilot", sku "Copilot
    // Premium Request"/"copilot_premium_request", unitType "Requests"/"requests".
    // Spaces and underscores in the sku are normalized away so both label forms
    // match a single comparison. Only these rows count toward the consumption
    // total; netQuantity is ignored on purpose (observed 0/null).
    public static bool IsCopilotPremiumRequestUsage(GitHubBillingUsageItem item)
        => string.Equals(item.Product, "copilot", StringComparison.OrdinalIgnoreCase)
            && NormalizeSku(item.Sku) == "copilotpremiumrequest"
            && string.Equals(item.UnitType, "requests", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSku(string? sku)
        => sku is null
            ? string.Empty
            : sku.Replace(" ", string.Empty, StringComparison.Ordinal)
                 .Replace("_", string.Empty, StringComparison.Ordinal)
                 .ToLowerInvariant();

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null)
        {
            return null;
        }

        return node.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();
    }

    private static double? GetDouble(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null)
        {
            return null;
        }

        if (node.GetValueKind() == System.Text.Json.JsonValueKind.Number)
        {
            return node.GetValue<double>();
        }

        var text = node.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
