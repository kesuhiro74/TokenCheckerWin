using System.Globalization;
using System.Text.Json;
using TokenChecker.Core.LocalCost;

namespace TokenChecker.Poc.LocalCost;

// POC runner for the local daily-cost feature. Invoked only when the POC is
// started with --daily-cost, so the default POC output is unchanged.
//
// Reads today's and this calendar month's (both local, expressed as UTC
// half-open intervals) token usage from the local Claude Code / Codex session
// logs in a SINGLE pass per provider via ComputeMany, prices it with
// ModelPricing, converts to JPY via UsdJpyRateProvider, and prints a single
// JSON object.
//
// Privacy: only numeric totals, model ids, the date and the rate are printed.
// Model ids are inside the CLAUDE.md allowance (numbers and model names only).
// No paths, logins, or other strings from the session logs ever reach the
// output.
internal static class DailyCostPocRunner
{
    public static async Task<int> RunAsync()
    {
        // Today's local day and the local calendar month to date, converted to
        // the UTC half-open intervals the readers operate on.
        var today = DateTime.Today;
        var monthStartLocal = new DateTime(today.Year, today.Month, 1);
        var dayStartUtc = new DateTimeOffset(today).ToUniversalTime();
        var monthStartUtc = new DateTimeOffset(monthStartLocal).ToUniversalTime();

        var intervals = new[]
        {
            new CostInterval(dayStartUtc, dayStartUtc.AddDays(1)),
            new CostInterval(monthStartUtc, dayStartUtc.AddDays(1))
        };

        var claude = ClaudeDailyCostReader.ComputeMany(
            ClaudeDailyCostReader.ResolveProjectsDirectory(), intervals);
        var codex = CodexDailyCostReader.ComputeMany(
            CodexDailyCostReader.ResolveSessionsDirectory(), intervals);

        var rate = await new UsdJpyRateProvider().GetRateAsync().ConfigureAwait(false);

        var output = new
        {
            date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Local calendar dates (the intervals themselves are UTC).
            monthStart = monthStartLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rateJpy = rate.RateJpy,
            rateIsFallback = rate.IsFallback,
            claude = new { today = Describe(claude[0], rate.RateJpy), month = Describe(claude[1], rate.RateJpy) },
            codex = new { today = Describe(codex[0], rate.RateJpy), month = Describe(codex[1], rate.RateJpy) }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        Console.WriteLine(JsonSerializer.Serialize(output, options));
        return 0;
    }

    private static object Describe(DailyCostResult result, decimal rateJpy)
        => new
        {
            costUsd = decimal.Round(result.CostUsd, 4, MidpointRounding.AwayFromZero),
            costJpy = decimal.Round(result.CostUsd * rateJpy, 0, MidpointRounding.AwayFromZero),
            inputTokens = result.InputTokens,
            outputTokens = result.OutputTokens,
            cacheWriteTokens = result.CacheWriteTokens,
            cacheReadTokens = result.CacheReadTokens,
            events = result.EventCount,
            unknownModelEvents = result.UnknownModelEvents,
            // Diagnostic: which model ids have no ModelPricing entry, and how
            // many events each one accounted for. Empty when every event's
            // model was priced. Numbers and model ids only (CLAUDE.md allows
            // this); no message content, cwd, or paths.
            unknownModelEventsById = result.UnknownModelEventsById
                .OrderByDescending(entry => entry.Value)
                .ToDictionary(entry => entry.Key, entry => entry.Value),
            costByModelUsd = result.CostByModelUsd
                .OrderByDescending(entry => entry.Value)
                .ToDictionary(
                    entry => entry.Key,
                    entry => decimal.Round(entry.Value, 4, MidpointRounding.AwayFromZero))
        };
}
