using TokenChecker.Core.LocalCost;

namespace TokenChecker.App;

// Per-service today's spend in JPY for the status cards. Null = unknown (no
// session data for the day, or the cost computation failed) -> the card hides
// its daily-cost label.
internal sealed record DailyCostsView(decimal? ClaudeJpy, decimal? CodexJpy);

// Computes today's local-session spend (Claude projects logs + Codex session
// logs, both read by TokenChecker.Core.LocalCost) converted to JPY. The log
// scan re-reads the whole day's files, so results are cached and recomputed at
// most every 10 minutes (or on demand via force=true, e.g. "今すぐ更新").
//
// This service must NEVER throw or write diagnostics: a cost-reading failure
// must not take down the usage display (same isolation philosophy as
// UsageAggregator), so every failure path collapses to (null, null).
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
            return new DailyCostsView(null, null);
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
        // "Today" is the LOCAL calendar day, converted to the UTC range the
        // readers filter on.
        var startLocal = new DateTimeOffset(DateTime.Today);
        var startUtc = startLocal.ToUniversalTime();
        var endUtc = startUtc.AddDays(1);

        // The readers do synchronous file I/O over the day's logs; run them off
        // the UI thread, in parallel with each other and with the rate fetch.
        var claudeTask = Task.Run(
            () => ClaudeDailyCostReader.Compute(ClaudeDailyCostReader.ResolveProjectsDirectory(), startUtc, endUtc),
            cancellationToken);
        var codexTask = Task.Run(
            () => CodexDailyCostReader.Compute(CodexDailyCostReader.ResolveSessionsDirectory(), startUtc, endUtc),
            cancellationToken);
        var rateTask = _rateProvider.GetRateAsync(cancellationToken);

        await Task.WhenAll(claudeTask, codexTask, rateTask).ConfigureAwait(false);

        var rate = rateTask.Result;
        return new DailyCostsView(ToJpy(claudeTask.Result, rate), ToJpy(codexTask.Result, rate));
    }

    // No rounding here: display formatting (N0) owns the rounding.
    private static decimal? ToJpy(DailyCostResult result, UsdJpyRate rate)
        => result.HasData ? result.CostUsd * rate.RateJpy : null;
}
