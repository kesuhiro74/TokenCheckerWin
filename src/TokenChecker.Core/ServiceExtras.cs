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

// One model's share of a monthly credit consumption (GitHub Copilot's
// usageItems[].model). Label is the model DISPLAY name exactly as the API
// reported it, or null when the row carried no model (the fallback billing
// endpoint omits the field); Credits is the number of credits attributed to it.
//
// Numbers and a model name only — no ids, repositories, emails or paths.
public sealed record ModelCredits(
    string? Label,
    decimal Credits);

// One UTC calendar day's credit consumption (GitHub Copilot's
// usageItems[].date grouped by UTC day). GitHub reports one usage row per UTC
// day that had usage, so a day without usage simply has no entry here.
//
// A date and a number only — no repository, model, id or path.
public sealed record DailyCredits(
    DateOnly UtcDate,
    decimal Credits);

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
    DateTimeOffset? ResetCreditsNextExpiry = null,
    // GitHub Copilot only: this month's credit consumption split by model.
    // Appended last so every existing construction site and every previously
    // written last_usage.json keeps working.
    IReadOnlyList<ModelCredits>? ModelCredits = null,
    // GitHub Copilot only: this month's credit consumption per UTC calendar
    // day, for the card's daily bar chart. Appended last for the same
    // backward-compatibility reason as ModelCredits.
    IReadOnlyList<DailyCredits>? DailyCredits = null,
    // GitHub Copilot only: the monthly AI-credit allowance auto-detected from
    // GitHub (the unofficial /copilot_internal/user entitlement). A NUMBER
    // only; when absent the App falls back to the plan picked in settings.
    // Appended last for the same backward-compatibility reason as above.
    decimal? CreditAllowance = null)
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
            && ResetCreditsNextExpiry is null
            && (ModelCredits is null || ModelCredits.Count == 0)
            && (DailyCredits is null || DailyCredits.Count == 0)
            && CreditAllowance is null;
}
