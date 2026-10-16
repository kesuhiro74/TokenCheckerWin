using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

// Pure JSON -> ServiceExtras translation for the Codex app-server's
// account/rateLimits/read result, kept separate from CodexUsageProvider so it
// can be unit-tested against recorded responses without launching an
// app-server. Public for the same reason CodexAccountClassifier is: Core has
// no InternalsVisibleTo.
//
// Response shape as of codex-cli 0.156.1:
//   rateLimits                   : snapshot (single bucket) - preferred
//   rateLimitsByLimitId.<id>     : snapshot per bucket - fallback
//   snapshot.planType            : plan enum
//   snapshot.credits             : { hasCredits, unlimited, balance: string|null } | null
//   snapshot.individualLimit     : { limit: string, used: string,
//                                    remainingPercent: int, resetsAt: unix s } | null
//   rateLimitResetCredits        : { availableCount: int,
//                                    credits: [{ status, expiresAt: unix s|null, ... }] }
//
// Privacy: only numbers, dates and the plan enum are read. The reset-credit
// id / title / description and any account id are never touched, and
// account/rateLimitResetCredit/consume is never called (it would spend a
// reset credit).
public static class CodexRateLimitsExtrasParser
{
    // planTypeFromAccount is account/read's planType, used when the rate-limit
    // snapshot does not repeat it.
    public static ServiceExtras Parse(JsonNode? rateLimitsResponse, string? planTypeFromAccount)
    {
        if (rateLimitsResponse is null)
        {
            return new ServiceExtras(PlanLabel: CodexPlanLabel.From(planTypeFromAccount));
        }

        var snapshot = SelectSnapshot(rateLimitsResponse);

        var planLabel = CodexPlanLabel.From(GetString(snapshot?["planType"]))
            ?? CodexPlanLabel.From(planTypeFromAccount);

        var (creditsBalance, creditsUnlimited) = ParseCredits(snapshot?["credits"]);
        var resetCredits = ParseResetCredits(rateLimitsResponse["rateLimitResetCredits"]);

        return new ServiceExtras(
            PlanLabel: planLabel,
            ScopedLimits: null,
            ExtraSpend: ParseIndividualLimit(snapshot?["individualLimit"]),
            CreditsBalance: creditsBalance,
            CreditsUnlimited: creditsUnlimited,
            ResetCreditsAvailable: resetCredits.AvailableCount,
            ResetCreditsNextExpiry: resetCredits.NextExpiry);
    }

    // The single `rateLimits` bucket is authoritative; rateLimitsByLimitId is
    // only consulted when it is absent (same precedence the window parser uses
    // in reverse, but here a single plan-level view is what we want).
    private static JsonObject? SelectSnapshot(JsonNode response)
    {
        if (response["rateLimits"] is JsonObject single)
        {
            return single;
        }

        if (response["rateLimitsByLimitId"] is JsonObject byLimitId)
        {
            foreach (var item in byLimitId)
            {
                if (item.Value is JsonObject snapshot)
                {
                    return snapshot;
                }
            }
        }

        return null;
    }

    private static (decimal? Balance, bool Unlimited) ParseCredits(JsonNode? node)
    {
        if (node is not JsonObject credits)
        {
            return (null, false);
        }

        var unlimited = GetBool(credits["unlimited"]) == true;
        var hasCredits = GetBool(credits["hasCredits"]) == true;

        // Only report a balance when the account actually has credits (or an
        // unlimited grant); otherwise the field is meaningless noise.
        var balance = hasCredits || unlimited ? GetDecimal(credits["balance"]) : null;

        return (balance, unlimited);
    }

    // The individual spend limit is denominated in credits, not currency, so
    // the unit is reported as "credits".
    private static ExtraSpend? ParseIndividualLimit(JsonNode? node)
    {
        if (node is not JsonObject individualLimit)
        {
            return null;
        }

        var used = GetDecimal(individualLimit["used"]);
        var limit = GetDecimal(individualLimit["limit"]);
        if (used is null && limit is null)
        {
            return null;
        }

        return new ExtraSpend(used ?? 0m, limit, "credits");
    }

    private static (int? AvailableCount, DateTimeOffset? NextExpiry) ParseResetCredits(JsonNode? node)
    {
        if (node is not JsonObject resetCredits)
        {
            return (null, null);
        }

        var availableCount = GetInt(resetCredits["availableCount"]);

        DateTimeOffset? nextExpiry = null;
        if (resetCredits["credits"] is JsonArray credits)
        {
            foreach (var credit in credits)
            {
                if (credit is not JsonObject item
                    || !string.Equals(GetString(item["status"]), "available", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var expiresAt = GetUnixSeconds(item["expiresAt"]);
                if (expiresAt is not null && (nextExpiry is null || expiresAt < nextExpiry))
                {
                    nextExpiry = expiresAt;
                }
            }
        }

        return (availableCount, nextExpiry);
    }

    private static DateTimeOffset? GetUnixSeconds(JsonNode? node)
    {
        var seconds = GetDecimal(node);
        if (seconds is null)
        {
            return null;
        }

        var value = (long)decimal.Truncate(seconds.Value);
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    private static bool? GetBool(JsonNode? node)
        => node?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    // The app-server reports credit amounts as STRINGS (balance, limit, used),
    // so accept both forms. decimal, never double: these are money-like values.
    private static decimal? GetDecimal(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.Number)
        {
            try
            {
                return node.GetValue<decimal>();
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            {
                return null;
            }
        }

        var text = GetString(node);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? GetInt(JsonNode? node)
    {
        var value = GetDecimal(node);
        if (value is null || value.Value > int.MaxValue || value.Value < int.MinValue)
        {
            return null;
        }

        return (int)decimal.Truncate(value.Value);
    }
}
