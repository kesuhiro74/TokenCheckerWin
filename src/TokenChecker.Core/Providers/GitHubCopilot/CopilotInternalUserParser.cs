using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// The plan name + monthly AI-credit allowance a Copilot account is entitled to,
// as auto-detected from GitHub. Label is a DISPLAY name taken from the mapping
// table below (never built from the raw plan code), CreditAllowance is the
// bundled monthly AI credits. Both optional: whatever could not be detected is
// null and the App falls back to the plan picked in settings.
public sealed record CopilotAccountPlan(
    string? PlanLabel,
    decimal? CreditAllowance);

// Parses the UNOFFICIAL, UNDOCUMENTED GET https://api.github.com/copilot_internal/user
// response down to the two display values the App wants: the plan name and the
// bundled monthly AI-credit allowance. This replaces the manual plan selection
// whenever the endpoint answers; it is NOT a documented API, so it may change
// shape or disappear at any time and every failure mode must degrade to the
// settings-based plan (the provider treats the whole read as best-effort).
//
// Privacy: only an explicit ALLOWLIST of fields is ever touched —
//   copilot_plan
//   quota_snapshots.premium_interactions.{entitlement, unlimited, remaining}
// Everything else the response carries (login, id, analytics_tracking_id,
// organization_list / organization_login_list, enterprise_list, endpoints,
// assigned_date, te, ...) is NEVER read, never copied into a variable, never
// logged, never displayed and never persisted. `remaining` is read for the
// consistency check documented on ReadRemaining only; it is not returned,
// displayed or stored. The response body never reaches a diagnostic Message.
//
// Why premium_interactions is the AI-credit meter: on a real Max account it
// reported entitlement=20000 / remaining=5022, and entitlement - remaining
// (14,978) matched the billing API's AI-credit total (14,977.95) to the credit.
// The `chat` and `completions` snapshots are unlimited (entitlement=0) on that
// plan and are deliberately ignored.
public static class CopilotInternalUserParser
{
    // Plan code -> display label.
    //
    // VERIFIED: "individual_max" -> "Copilot Max" (observed on a real account
    // whose entitlement, 20000, matches the public Max allowance).
    //
    // UNVERIFIED (pattern-inferred from the verified code plus GitHub's public
    // tier names Free / Pro / Pro+ / Max): the other individual_* codes and
    // "free". They are listed because getting one of them right costs nothing
    // and getting it wrong only mislabels the pill — the allowance itself comes
    // from `entitlement`, not from this table.
    //
    // Deliberately NOT mapped: business / enterprise / anything unknown. Org- or
    // enterprise-managed Copilot usage does not appear on the personal billing
    // endpoint this provider reads, so labelling it would promise a meter that
    // cannot be filled. An unmapped code yields a null label and the App keeps
    // the title from settings. No label is ever derived from the code string
    // itself.
    public static string? PlanLabel(string? planCode)
        => GitHubBillingUsageParser.Normalize(planCode) switch
        {
            "individualmax" => "Copilot Max",                   // VERIFIED
            "individualproplus" or "individualpro+" => "Copilot Pro+", // UNVERIFIED
            "individualpro" => "Copilot Pro",                    // UNVERIFIED
            // The paid individual tier was called "Copilot Individual" before it
            // was renamed "Copilot Pro", so a bare "individual" is Pro.
            "individual" => "Copilot Pro",                       // UNVERIFIED
            "free" or "individualfree" or "copilotfree" => "Copilot Free", // UNVERIFIED
            _ => null
        };

    // Reads the allowlisted fields out of a /copilot_internal/user response.
    // Returns false (and an all-null plan) when neither a known plan label nor a
    // usable allowance could be found, so the caller can keep its current
    // behavior unchanged.
    //
    // The allowance is reported ONLY when premium_interactions exists, is not
    // unlimited, and carries a positive entitlement. An unlimited or zero
    // entitlement is not an allowance (it is "no credit meter"), and a plan with
    // no premium_interactions snapshot reports none either.
    public static bool TryParse(JsonNode? root, out CopilotAccountPlan plan)
    {
        // Every step narrows to a JsonObject first: indexing a JsonArray or a
        // JsonValue by name THROWS, and an unofficial endpoint is exactly where
        // an unexpected shape should degrade quietly instead.
        var obj = root as JsonObject;
        var label = PlanLabel(ReadString(Property(obj, "copilot_plan")));
        var allowance = ReadAllowance(
            Property(Property(obj, "quota_snapshots") as JsonObject, "premium_interactions") as JsonObject);

        plan = new CopilotAccountPlan(label, allowance);
        return label is not null || allowance is not null;
    }

    private static JsonNode? Property(JsonObject? obj, string name)
        => obj is not null && obj.TryGetPropertyValue(name, out var value) ? value : null;

    // Folds an auto-detected plan into the extras the billing parser produced,
    // so the provider stays free of extras-shaping logic:
    //  - nothing detected                 -> the extras are returned untouched;
    //  - detected but no extras yet       -> a fresh extras carrying only the
    //                                        plan label / allowance;
    //  - both                             -> the extras with the plan attached.
    // A non-null existing PlanLabel is never overwritten (the billing read never
    // sets one today, but that keeps the helper total).
    public static ServiceExtras? Merge(ServiceExtras? extras, CopilotAccountPlan? plan)
    {
        if (plan is null || (plan.PlanLabel is null && plan.CreditAllowance is null))
        {
            return extras;
        }

        var merged = (extras ?? new ServiceExtras()) with
        {
            PlanLabel = extras?.PlanLabel ?? plan.PlanLabel,
            CreditAllowance = plan.CreditAllowance ?? extras?.CreditAllowance
        };

        return merged.IsEmpty ? null : merged;
    }

    private static decimal? ReadAllowance(JsonObject? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        if (ReadBool(Property(snapshot, "unlimited")) == true)
        {
            return null;
        }

        var entitlement = ReadDecimal(Property(snapshot, "entitlement"));
        return entitlement is > 0m ? entitlement : null;
    }

    // NOTE on `remaining`: it is allowlisted for reading (a test asserts the
    // consistency relation "entitlement - remaining == the billing API's
    // AI-credit total" that identified premium_interactions as the AI-credit
    // meter), but this parser deliberately exposes NO accessor for it — nothing
    // in the App needs it, so it is never displayed, logged or persisted.

    private static string? ReadString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    private static bool? ReadBool(JsonNode? node)
        => node?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    // Same no-double rule as the billing parser: a number node's ToString() is
    // its JSON literal, which decimal.TryParse consumes exactly.
    private static decimal? ReadDecimal(JsonNode? node)
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
