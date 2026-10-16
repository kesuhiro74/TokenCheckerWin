using TokenChecker.Core.LocalCost;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the local Codex rollout aggregation: the dated directory layout,
// uncached/cached input split, turn_context model tracking, the null-info
// guard, replay dedup, and malformed-line handling. Fixture .jsonl files are
// written to a random temp directory and removed on dispose.
public sealed class CodexDailyCostReaderTests : IDisposable
{
    // Interval fixed in the past so freshly written fixture files always pass
    // the last-write-time prefilter.
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndUtc = StartUtc.AddDays(1);

    private readonly string _root;

    public CodexDailyCostReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TokenCheckerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private string WriteRollout(int year, int month, int day, string fileName, params string[] lines)
    {
        var directory = Path.Combine(_root, $"{year:D4}", $"{month:D2}", $"{day:D2}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string TurnContextLine(string timestamp, string model)
        => $"{{\"timestamp\":\"{timestamp}\",\"type\":\"turn_context\",\"payload\":{{\"model\":\"{model}\"}}}}";

    private static string TokenCountLine(
        string timestamp,
        long input = 5000,
        long cached = 4000,
        long output = 300,
        long total = 5300)
        => $"{{\"timestamp\":\"{timestamp}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\","
            + $"\"info\":{{\"last_token_usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},"
            + $"\"output_tokens\":{output},\"reasoning_output_tokens\":120,\"total_tokens\":{total}}}}}}}}}";

    [Fact]
    public void Compute_SplitsUncachedAndCachedInput()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TurnContextLine("2026-01-10T00:59:00Z", "gpt-5.5"),
            TokenCountLine("2026-01-10T01:00:00Z", input: 5000, cached: 4000, output: 300, total: 5300));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.True(result.HasData);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(1000, result.InputTokens);       // 5000 - 4000 uncached
        Assert.Equal(4000, result.CacheReadTokens);
        Assert.Equal(0, result.CacheWriteTokens);
        Assert.Equal(300, result.OutputTokens);       // reasoning is NOT added on top
        // gpt-5.5: (1000*5 + 300*30 + 4000*0.5) / 1e6 = 16000 / 1e6.
        Assert.Equal(0.016m, result.CostUsd);
    }

    [Fact]
    public void Compute_WithoutTurnContext_UsesDefaultGpt5()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TokenCountLine("2026-01-10T01:00:00Z", input: 2_000_000, cached: 0, output: 0, total: 2_000_000));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
        // gpt-5 fallback: 2,000,000 * 1.25 / 1e6.
        Assert.Equal(2.5m, result.CostUsd);
    }

    [Fact]
    public void Compute_TurnContextModel_DoesNotLeakIntoNextFile()
    {
        // File a switches to gpt-5.5; file b has no turn_context and must fall
        // back to gpt-5.
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TurnContextLine("2026-01-10T00:59:00Z", "gpt-5.5"),
            TokenCountLine("2026-01-10T01:00:00Z", input: 1_000_000, cached: 0, output: 0, total: 1_000_000));
        WriteRollout(2026, 1, 10, "rollout-b.jsonl",
            TokenCountLine("2026-01-10T02:00:00Z", input: 1_000_000, cached: 0, output: 0, total: 1_000_001));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(2, result.EventCount);
        // 5.00 (gpt-5.5) + 1.25 (gpt-5 default).
        Assert.Equal(6.25m, result.CostUsd);
    }

    [Fact]
    public void Compute_NullInfo_IsSkipped()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            "{\"timestamp\":\"2026-01-10T01:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}",
            TokenCountLine("2026-01-10T01:01:00Z"));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_ReplayedEvents_AreDeduplicated()
    {
        // Resuming a session replays the same events into a new rollout file:
        // identical timestamp + total_tokens must count once.
        var lines = new[]
        {
            TurnContextLine("2026-01-10T00:59:00Z", "gpt-5.5"),
            TokenCountLine("2026-01-10T01:00:00Z", total: 5300)
        };
        WriteRollout(2026, 1, 10, "rollout-a.jsonl", lines);
        WriteRollout(2026, 1, 10, "rollout-b.jsonl", lines);

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(1000, result.InputTokens);
    }

    [Fact]
    public void Compute_OutOfRangeTimestamps_AreExcluded()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TokenCountLine("2026-01-10T00:00:00Z", total: 1),    // == start: included
            TokenCountLine("2026-01-11T00:00:00Z", total: 2),    // == end: excluded
            TokenCountLine("2026-01-09T23:59:59Z", total: 3));   // before start: excluded

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_ScansAdjacentDayDirectories()
    {
        // An event just past UTC midnight can land in the previous local-day
        // directory; the ±1 day directory scan must still find it.
        WriteRollout(2026, 1, 9, "rollout-late.jsonl",
            TokenCountLine("2026-01-10T00:30:00Z"));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_IgnoresNonRolloutFiles()
    {
        WriteRollout(2026, 1, 10, "notes.jsonl",
            TokenCountLine("2026-01-10T01:00:00Z"));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(DailyCostResult.Empty, result);
    }

    [Fact]
    public void Compute_SkipsMalformedLines()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\", broken",   // passes prefilter, fails parse
            TokenCountLine("2026-01-10T01:00:00Z"));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        var result = CodexDailyCostReader.Compute(missing, StartUtc, EndUtc);

        Assert.Equal(DailyCostResult.Empty, result);
    }

    [Fact]
    public void Compute_NegativeUncached_ClampsToZero()
    {
        // Defensive: cached_input_tokens larger than input_tokens must not
        // produce a negative billed-input count.
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TokenCountLine("2026-01-10T01:00:00Z", input: 100, cached: 500, output: 0, total: 100));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(500, result.CacheReadTokens);
    }

    [Fact]
    public void Compute_ReportsPerModelCostBreakdown()
    {
        WriteRollout(2026, 1, 10, "rollout-a.jsonl",
            TurnContextLine("2026-01-10T00:30:00Z", "gpt-5.6-terra"),
            TokenCountLine("2026-01-10T01:00:00Z", input: 1000, cached: 0, output: 1000, total: 2000),
            TurnContextLine("2026-01-10T02:00:00Z", "gpt-5.6-luna"),
            TokenCountLine("2026-01-10T02:30:00Z", input: 1000, cached: 0, output: 1000, total: 2001),
            TurnContextLine("2026-01-10T03:00:00Z", "mystery-model-9"),
            TokenCountLine("2026-01-10T03:30:00Z", input: 1000, cached: 0, output: 1000, total: 2002));

        var result = CodexDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(2, result.CostByModelUsd.Count);
        // gpt-5.6-terra: (1000*2 + 1000*12) / 1e6.
        Assert.Equal(0.014m, result.CostByModelUsd["gpt-5.6-terra"]);
        // gpt-5.6-luna: (1000*0.2 + 1000*1.2) / 1e6.
        Assert.Equal(0.0014m, result.CostByModelUsd["gpt-5.6-luna"]);
        Assert.Equal(1, result.UnknownModelEvents);
        Assert.Equal(result.CostUsd, result.CostByModelUsd.Values.Sum());
        Assert.Equal(1, result.UnknownModelEventsById["mystery-model-9"]);
    }

    [Fact]
    public void ComputeMany_WalksEveryDayDirectoryFromTheMonthStart()
    {
        var monthStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var intervals = new[]
        {
            new CostInterval(StartUtc, EndUtc),       // "today"
            new CostInterval(monthStart, EndUtc)      // "this month", contains today
        };

        // Three separate day directories: only the month interval reaches the
        // first two, and the widest span has to drive the directory walk.
        WriteRollout(2026, 1, 2, "rollout-a.jsonl", TokenCountLine("2026-01-02T01:00:00Z", total: 1));
        WriteRollout(2026, 1, 6, "rollout-b.jsonl", TokenCountLine("2026-01-06T01:00:00Z", total: 2));
        WriteRollout(2026, 1, 10, "rollout-c.jsonl", TokenCountLine("2026-01-10T01:00:00Z", total: 3));

        var results = CodexDailyCostReader.ComputeMany(_root, intervals);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].EventCount);
        Assert.Equal(3, results[1].EventCount);
        Assert.True(results[0].CostUsd <= results[1].CostUsd);
    }

    [Fact]
    public void ComputeMany_DedupIsGlobal_ButCountsOncePerInterval()
    {
        var monthStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // A resumed session replays the same event into a second rollout.
        WriteRollout(2026, 1, 10, "rollout-a.jsonl", TokenCountLine("2026-01-10T01:00:00Z"));
        WriteRollout(2026, 1, 10, "rollout-b.jsonl", TokenCountLine("2026-01-10T01:00:00Z"));

        var results = CodexDailyCostReader.ComputeMany(
            _root,
            new[] { new CostInterval(StartUtc, EndUtc), new CostInterval(monthStart, EndUtc) });

        Assert.Equal(1, results[0].EventCount);
        Assert.Equal(1, results[1].EventCount);
    }

    [Fact]
    public void ComputeMany_NoIntervals_ReturnsEmptyList()
        => Assert.Empty(CodexDailyCostReader.ComputeMany(_root, Array.Empty<CostInterval>()));
}
