using System.Globalization;
using System.Text.Json;

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

internal enum CopilotPrediction
{
    Insufficient,     // not enough data, or no allowance
    ReachesThisMonth, // FullDateLocal is set
    NotThisMonth,     // at this pace the monthly reset arrives before 100%
    AlreadyFull       // already at/over 100%
}

internal sealed record CopilotInsights(
    CopilotPrediction Prediction,
    DateTimeOffset? FullDateLocal,
    long? TodayDeltaCredits,   // null = no baseline yet (未計測)
    double? TodayDeltaPercent); // null = no allowance

internal sealed class CopilotUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public CopilotUsageStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(appData, "TokenCheckerWin", "copilot_usage.json");
    }

    // Test seam: back the store with an explicit file path so unit tests never
    // read or write the real copilot_usage.json. Schema and load/save behavior
    // are unchanged — only the location differs.
    internal CopilotUsageStore(string path) => _path = path;

    public CopilotUsageRecord? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CopilotUsageRecord>(File.ReadAllText(_path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CopilotUsageRecord record)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(record, JsonOptions));
        }
        catch
        {
            // Persistence must never take down the tray app.
        }
    }
}

internal sealed class CopilotUsageTracker
{
    // The day-window boundary is 09:00 local, matching the monthly reset's
    // time-of-day (ResetAtUtc is 1st-of-month 00:00 UTC, which renders as 9:00 in
    // JST). "Since today's 09:00" therefore aligns with the reset clock.
    private static readonly TimeSpan DayBoundary = TimeSpan.FromHours(9);

    // Below this many elapsed days the average-pace projection is too noisy to show.
    private const double MinElapsedDaysForPrediction = 0.5;

    private readonly CopilotUsageStore _store;
    private CopilotUsageRecord _record;

    public CopilotUsageTracker() : this(new CopilotUsageStore())
    {
    }

    // Test seam: inject a store backed by a temp file. No behavior change — the
    // default constructor uses the real %APPDATA% store.
    internal CopilotUsageTracker(CopilotUsageStore store)
    {
        _store = store;
        _record = _store.Load() ?? new CopilotUsageRecord();
    }

    // Records a fresh monthly-used sample, updates the persisted baseline, and
    // returns the derived insights. Call ONLY for a genuinely fresh Available
    // sample (never for fallback/last-success values).
    public CopilotInsights Observe(long usedNow, int? allowance, DateTimeOffset nowUtc)
    {
        var monthKey = nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var sameMonth = string.Equals(_record.Month, monthKey, StringComparison.Ordinal);

        var nowLocal = nowUtc.ToLocalTime().LocalDateTime;
        var windowDate = nowLocal.TimeOfDay >= DayBoundary ? nowLocal.Date : nowLocal.Date.AddDays(-1);
        var windowKey = windowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var windowStartLocal = windowDate + DayBoundary;

        long? baselineUsed;
        if (sameMonth && string.Equals(_record.DayWindowLocalDate, windowKey, StringComparison.Ordinal))
        {
            // Baseline already established for the current 09:00 window.
            baselineUsed = _record.DayBaselineUsed;
        }
        else if (sameMonth
            && _record.LastUsed is long prevUsed
            && _record.LastCapturedAtUtc is DateTimeOffset prevAt
            && prevAt.ToLocalTime().LocalDateTime is var prevLocal
            && prevLocal < windowStartLocal
            && prevLocal >= windowStartLocal.AddDays(-1))
        {
            // First sample of a new window: the last pre-window sample's used is the
            // best proxy for "used at 09:00", so adopt it as this window's baseline.
            // Require it to be RECENT (within the immediately preceding 09:00 window):
            // a days-old sample (app closed across several 09:00 boundaries) would
            // otherwise turn the whole multi-day growth into today's delta. When it
            // is stale we fall through to 未計測 rather than over-count.
            baselineUsed = prevUsed;
            _record.DayWindowLocalDate = windowKey;
            _record.DayBaselineUsed = prevUsed;
        }
        else
        {
            // No valid (recent) pre-window baseline (fresh install, app not running
            // before 09:00, a multi-day gap, or a month change): cannot measure
            // "since 09:00" yet. Record the window so future samples can establish one.
            baselineUsed = null;
            _record.DayWindowLocalDate = windowKey;
            _record.DayBaselineUsed = null;
        }

        // Roll the last sample forward and persist (month change adopts the new month).
        _record.Month = monthKey;
        _record.LastUsed = usedNow;
        _record.LastCapturedAtUtc = nowUtc;
        _store.Save(_record);

        long? todayDelta = null;
        double? todayPercent = null;
        if (baselineUsed is long baseline)
        {
            // A drop (month roll / API correction / anomaly) must never show negative.
            var delta = Math.Max(0L, usedNow - baseline);
            todayDelta = delta;
            if (allowance is int cap && cap > 0)
            {
                todayPercent = delta / (double)cap * 100d;
            }
        }

        var (prediction, fullDate) = PredictFull(usedNow, allowance, nowUtc);
        return new CopilotInsights(prediction, fullDate, todayDelta, todayPercent);
    }

    private static (CopilotPrediction Prediction, DateTimeOffset? FullDateLocal) PredictFull(
        long usedNow, int? allowance, DateTimeOffset nowUtc)
    {
        if (allowance is not int cap || cap <= 0 || usedNow <= 0)
        {
            return (CopilotPrediction.Insufficient, null);
        }

        // Anchor the average pace to the LOCAL calendar 1st-of-month at 00:00 (the
        // user's "その月の1日"), and project from the usage ratio at this moment:
        // the date of 100% = monthStart + elapsedDays * (cap / usedNow).
        var nowLocal = nowUtc.ToLocalTime();
        var monthStartLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, nowLocal.Offset);
        var resetLocal = monthStartLocal.AddMonths(1);
        var elapsedDays = (nowLocal - monthStartLocal).TotalDays;
        if (elapsedDays < MinElapsedDaysForPrediction)
        {
            return (CopilotPrediction.Insufficient, null);
        }

        var remaining = cap - usedNow;
        if (remaining <= 0)
        {
            return (CopilotPrediction.AlreadyFull, null);
        }

        var dailyBurn = usedNow / elapsedDays;
        if (dailyBurn <= 0)
        {
            return (CopilotPrediction.Insufficient, null);
        }

        var fullLocal = nowLocal.AddDays(remaining / dailyBurn);
        if (fullLocal >= resetLocal)
        {
            // Credits reset monthly, so a projected date past the reset would be
            // wrong — report "won't reach this month" instead of a next-month date.
            return (CopilotPrediction.NotThisMonth, null);
        }

        return (CopilotPrediction.ReachesThisMonth, fullLocal);
    }
}
