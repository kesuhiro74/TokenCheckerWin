using System.Globalization;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// Pure JSON parsing helpers for the GitHub Copilot POC, kept separate from the
// provider because the billing usage schema is volatile (usage-based billing
// rolls out 2026-06-01) and isolating the parsing makes it cheap to iterate.
//
// These types are public (not internal) on purpose: the POC runner lives in the
// separate TokenChecker.Poc assembly and reuses ParseUsageItems/LooksLikeCopilot
// to dump the real fields it observes. This is experimental surface only.
public static class GitHubBillingUsageParser
{
    // Only the fields the POC needs to observe for the final mapping decision.
    // repositoryName is intentionally NOT captured so it can never leak through
    // the structured output or the item listing.
    public sealed record GitHubBillingUsageItem(
        string? Product,
        string? Sku,
        string? UnitType,
        double? Quantity,
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
                GetDouble(node["netQuantity"]),
                GetDouble(node["netAmount"])));
        }

        return parsed;
    }

    // Provisional Copilot match. The final rule is decided after observing the
    // real product/sku values via --raw; for now we match loosely so we don't
    // silently drop Copilot rows whose label changes (e.g. post-2026-06-01).
    public static bool LooksLikeCopilot(GitHubBillingUsageItem item)
        => ContainsCopilot(item.Product) || ContainsCopilot(item.Sku);

    private static bool ContainsCopilot(string? value)
        => value is not null && value.Contains("copilot", StringComparison.OrdinalIgnoreCase);

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
