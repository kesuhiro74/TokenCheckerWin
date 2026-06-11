namespace TokenChecker.App;

// Minimal persisted bookkeeping for the GitHub Copilot AI Credits sub-displays
// (100%-reach projection + "since today's 09:00" delta). The GitHub API only
// returns the running monthly total, so the App snapshots it and diffs locally.
//
// Stored in its own small file (copilot_usage.json) rather than last_usage.json:
// last_usage.json holds the shared UsageSnapshot schema (service -> RateLimitWindow,
// with Message nulled for privacy), and this Copilot-specific tracking (target
// month, the 09:00 day-window baseline, last sample time) does not map onto that
// model without polluting it. Only numbers and dates are stored — never tokens,
// logins, paths, or emails. The allowance is NOT duplicated here: settings.json is
// its single source of truth and percentages are always computed from the current
// CopilotCreditAllowance().
internal sealed class CopilotUsageRecord
{
    // yyyy-MM (UTC) the values below belong to.
    public string? Month { get; set; }
    public DateTimeOffset? LastCapturedAtUtc { get; set; }
    // Most recent monthly used credits observed.
    public long? LastUsed { get; set; }
    // yyyy-MM-dd (local) of the 09:00 day-window the baseline is for.
    public string? DayWindowLocalDate { get; set; }
    // Used credits at the start of that window (proxy: last pre-09:00 sample).
    public long? DayBaselineUsed { get; set; }
}
