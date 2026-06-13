using System.Globalization;
using System.Text.Json;
using TokenChecker.Core.LocalCost;

namespace TokenChecker.Poc.LocalCost;

// POC runner for the local daily-cost feature. Invoked only when the POC is
// started with --daily-cost, so the default POC output is unchanged.
//
// Reads today's (local midnight to local midnight, expressed in UTC) token
// usage from the local Claude Code / Codex session logs, prices it with
// ModelPricing, converts to JPY via UsdJpyRateProvider, and prints a single
// JSON object.
//
// Privacy: only numeric totals, the date, and the rate are printed. No paths,
// logins, or other strings from the session logs ever reach the output.
internal static class DailyCostPocRunner
{
    public static async Task<int> RunAsync()
    {
        // Today's local day, converted to the UTC half-open interval
        // [startUtc, endUtc) the readers operate on.
        var startLocal = new DateTimeOffset(DateTime.Today);
        var startUtc = startLocal.ToUniversalTime();
        var endUtc = startUtc.AddDays(1);

        var claude = ClaudeDailyCostReader.Compute(
            ClaudeDailyCostReader.ResolveProjectsDirectory(), startUtc, endUtc);
        var codex = CodexDailyCostReader.Compute(
            CodexDailyCostReader.ResolveSessionsDirectory(), startUtc, endUtc);

        var rate = await new UsdJpyRateProvider().GetRateAsync().ConfigureAwait(false);

        var output = new
        {
            date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rateJpy = rate.RateJpy,
            rateIsFallback = rate.IsFallback,
            claude = Describe(claude, rate.RateJpy),
            codex = Describe(codex, rate.RateJpy)
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
            unknownModelEvents = result.UnknownModelEvents
        };
}
