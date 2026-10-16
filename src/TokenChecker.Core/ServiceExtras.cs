using System.Text.Json.Serialization;

namespace TokenChecker.Core;

// One model-scoped or surface-scoped rate-limit entry (Claude's
// limits[].kind == "weekly_scoped", or the legacy seven_day_opus /
// seven_day_sonnet fields).
//
// These deliberately do NOT go into ServiceUsage.Windows: the App picks the
// weekly window by "first window with WindowDurationMins == 10080", so a
// scoped weekly entry mixed in there would be shown as THE weekly limit.
//
// Label is a display name only (e.g. a model display name such as "Fable");
// no ids, emails or paths.
public sealed record ScopedLimit(
    string Label,
    double? UsedPercent,
    DateTimeOffset? ResetAtUtc);

// Actual overage/extra spending for a service. Unit is a currency code
// ("USD", ...) for Claude's spend / extra_usage, or "credits" for Codex's
// individual spend limit. Numbers only.
public sealed record ExtraSpend(
    decimal Used,
    decimal? Limit,
    string Unit);

// Optional, display-only extras for a ServiceUsage: the subscription plan
// label, per-model weekly limits, real money/credit spend, and Codex's credit
// balance plus rate-limit reset credits.
//
// Privacy: every member is a number, a date, or a plan/model DISPLAY name.
// Nothing here carries a token, an email, an account id, a credit id or a
// path. All members are optional so a provider fills in only what its API
// actually returned, and every existing ServiceUsage construction site keeps
// working with Extras left null.
public sealed record ServiceExtras(
    string? PlanLabel = null,
    IReadOnlyList<ScopedLimit>? ScopedLimits = null,
    ExtraSpend? ExtraSpend = null,
    // Codex only: prepaid credit balance, when the account reports credits.
    decimal? CreditsBalance = null,
    bool CreditsUnlimited = false,
    // Codex only: unused rate-limit reset credits ("reset tickets") and the
    // earliest expiry among the available ones.
    int? ResetCreditsAvailable = null,
    DateTimeOffset? ResetCreditsNextExpiry = null)
{
    // True when there is nothing worth showing, so a provider can avoid
    // attaching an all-empty Extras. Derived, so it is kept out of the
    // serialized snapshot.
    [JsonIgnore]
    public bool IsEmpty
        => PlanLabel is null
            && (ScopedLimits is null || ScopedLimits.Count == 0)
            && ExtraSpend is null
            && CreditsBalance is null
            && !CreditsUnlimited
            && ResetCreditsAvailable is null
            && ResetCreditsNextExpiry is null;
}
