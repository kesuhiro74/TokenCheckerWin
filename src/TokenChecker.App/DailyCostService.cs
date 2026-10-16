using TokenChecker.Core.LocalCost;

namespace TokenChecker.App;

// One spend figure in both currencies, so the UI shows ¥ (Japanese) or $
// (English) per the active language without re-fetching the FX rate.
internal sealed record DailyCost(decimal Usd, decimal Jpy);

// One service's spend view: today (local calendar day), this month (local
// calendar month to date) and the month's per-model breakdown for the money
// row's tooltip. A null amount = unknown (no session data for the range, or the
// cost computation failed) -> the UI hides that figure.
internal sealed record ServiceCostView(
    DailyCost? Today,
    DailyCost? Month,
    IReadOnlyList<ModelCostLine> MonthByModel)
{
    public static readonly ServiceCostView Unknown =
        new(null, null, Array.Empty<ModelCostLine>());

    public bool HasAnyAmount => Today is not null || Month is not null;
}

// Per-service spend for the status cards/line/footer.
internal sealed record DailyCostsView(ServiceCostView? Claude, ServiceCostView? Codex)
{
    public static readonly DailyCostsView Empty = new(null, null);
}

// Computes local-session spend (Claude projects logs + Codex session logs, both
// read by TokenChecker.Core.LocalCost) for today AND this calendar month, in a
// single pass per provider (ComputeMany), converted to JPY. The log scan
// re-reads the month's files, so results are cached and recomputed at most
// every 10 minutes (or on demand via force=true, e.g. "今すぐ更新").
//
// This service must NEVER throw or write diagnostics: a cost-reading failure
// must not take down the usage display (same isolation philosophy as
// UsageAggregator), so every failure path collapses to an empty view.
internal sealed class DailyCostService
{
    private static readonly TimeSpan RecomputeInterval = TimeSpan.FromMinutes(10);

    // The USD->JPY rate provider keeps its own daily cache internally; hold a
    // single instance so that cache survives across refreshes.
    private readonly UsdJpyRateProvider _rateProvider = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DailyCostsView? _cached;
    private DateTimeOffset _computedAt;
    private DateTime _cachedDate;

    public async Task<DailyCostsView> GetAsync(bool force, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            // Serialize callers so overlapping refreshes don't scan the logs twice.
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            var today = DateTime.Today;
            if (!force
                && _cached is not null
                && _cachedDate == today
                && DateTimeOffset.Now - _computedAt < RecomputeInterval)
            {
                return _cached;
            }

            var result = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            _cached = result;
            _computedAt = DateTimeOffset.Now;
            _cachedDate = today;
            return result;
        }
        catch
        {
            // Swallow everything (including cancellation): cost display is
            // strictly best-effort and must never break the caller's refresh.
            return DailyCostsView.Empty;
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }

    private async Task<DailyCostsView> ComputeAsync(CancellationToken cancellationToken)
    {
        // "Today" is the LOCAL calendar day and "this month" the LOCAL calendar
        // month to date, both converted to the UTC ranges the readers filter on.
        // Index 0 = today, index 1 = month (same end instant, so today is always
        // a subset of the month).
        var intervals = BuildIntervals(DateTime.Today);

        // The readers do synchronous file I/O over the month's logs; run them off
        // the UI thread, in parallel with each other and with the rate fetch.
        var claudeTask = Task.Run(
            () => ClaudeDailyCostReader.ComputeMany(ClaudeDailyCostReader.ResolveProjectsDirectory(), intervals),
            cancellationToken);
        var codexTask = Task.Run(
            () => CodexDailyCostReader.ComputeMany(CodexDailyCostReader.ResolveSessionsDirectory(), intervals),
            cancellationToken);
        var rateTask = _rateProvider.GetRateAsync(cancellationToken);

        await Task.WhenAll(claudeTask, codexTask, rateTask).ConfigureAwait(false);

        var rate = rateTask.Result;
        return new DailyCostsView(ToView(claudeTask.Result, rate), ToView(codexTask.Result, rate));
    }

    // Test-visible seam for the today/month interval pair (the UI's definition
    // of "today" and "this month"), kept pure so it can be checked for any date.
    internal static IReadOnlyList<CostInterval> BuildIntervals(DateTime todayLocal)
    {
        var dayStartUtc = new DateTimeOffset(todayLocal.Date).ToUniversalTime();
        var monthStartUtc = new DateTimeOffset(new DateTime(todayLocal.Year, todayLocal.Month, 1)).ToUniversalTime();
        var endUtc = dayStartUtc.AddDays(1);
        return new[]
        {
            new CostInterval(dayStartUtc, endUtc),
            new CostInterval(monthStartUtc, endUtc)
        };
    }

    // Carries both currencies (USD straight from the reader, JPY via the rate);
    // display formatting owns the rounding.
    internal static ServiceCostView ToView(IReadOnlyList<DailyCostResult> results, UsdJpyRate rate)
    {
        if (results.Count < 2)
        {
            return ServiceCostView.Unknown;
        }

        var month = results[1];
        return new ServiceCostView(
            ToCost(results[0], rate),
            ToCost(month, rate),
            CostBreakdown.Build(month.CostByModelUsd, rate.RateJpy));
    }

    private static DailyCost? ToCost(DailyCostResult result, UsdJpyRate rate)
        => result.HasData ? new DailyCost(result.CostUsd, result.CostUsd * rate.RateJpy) : null;
}

// Sum of the month's spend across the services the window currently shows, for
// the status window's footer total. Pure so the visibility rules are testable:
// a hidden service never contributes, and a null result means "nothing to show"
// (the footer hides itself).
internal static class MonthlyTotal
{
    public static DailyCost? Sum(
        ServiceCostView? claude,
        bool showClaude,
        ServiceCostView? codex,
        bool showCodex)
    {
        var usd = 0m;
        var jpy = 0m;
        var any = false;

        if (showClaude && claude?.Month is { } claudeMonth)
        {
            usd += claudeMonth.Usd;
            jpy += claudeMonth.Jpy;
            any = true;
        }

        if (showCodex && codex?.Month is { } codexMonth)
        {
            usd += codexMonth.Usd;
            jpy += codexMonth.Jpy;
            any = true;
        }

        return any ? new DailyCost(usd, jpy) : null;
    }
}
