using TokenChecker.Core.LocalCost;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the local Claude Code session-log aggregation: interval
// boundaries, dedup, unknown models, malformed lines, the last-write-time
// prefilter, and subdirectory recursion. Fixture .jsonl files are written to
// a random temp directory and removed on dispose.
public sealed class ClaudeDailyCostReaderTests : IDisposable
{
    // Interval fixed in the past so freshly written fixture files always pass
    // the last-write-time prefilter.
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndUtc = StartUtc.AddDays(1);

    private readonly string _root;

    public ClaudeDailyCostReaderTests()
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

    private string WriteLog(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string AssistantLine(
        string timestamp,
        string model = "claude-opus-4-8",
        string? messageId = "msg_1",
        string? requestId = "req_1",
        long input = 10,
        long cacheWrite = 0,
        long cacheRead = 0,
        long output = 20)
    {
        var idPart = messageId is null ? "" : $"\"id\":\"{messageId}\",";
        var requestPart = requestId is null ? "" : $"\"requestId\":\"{requestId}\",";
        return $"{{\"type\":\"assistant\",\"timestamp\":\"{timestamp}\",{requestPart}"
            + $"\"message\":{{{idPart}\"model\":\"{model}\","
            + $"\"usage\":{{\"input_tokens\":{input},\"cache_creation_input_tokens\":{cacheWrite},"
            + $"\"cache_read_input_tokens\":{cacheRead},\"output_tokens\":{output}}}}}}}";
    }

    [Fact]
    public void Compute_AggregatesTokensAndCost()
    {
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T01:00:00Z", input: 12, cacheWrite: 3456, cacheRead: 78901, output: 234));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.True(result.HasData);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(234, result.OutputTokens);
        Assert.Equal(3456, result.CacheWriteTokens);
        Assert.Equal(78901, result.CacheReadTokens);
        // claude-opus-4-8: (12*5 + 234*25 + 3456*6.25 + 78901*0.5) / 1e6.
        Assert.Equal(0.0669605m, result.CostUsd);
        Assert.Equal(0, result.UnknownModelEvents);
    }

    [Fact]
    public void Compute_IncludesStartBoundary_ExcludesEndBoundary()
    {
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T00:00:00Z", messageId: "m1", requestId: "r1"),   // == start: included
            AssistantLine("2026-01-11T00:00:00Z", messageId: "m2", requestId: "r2"),   // == end: excluded
            AssistantLine("2026-01-09T23:59:59Z", messageId: "m3", requestId: "r3"),   // before start: excluded
            AssistantLine("2026-01-11T00:00:01Z", messageId: "m4", requestId: "r4"));  // after end: excluded

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(10, result.InputTokens);
    }

    [Fact]
    public void Compute_DeduplicatesByMessageIdAndRequestId()
    {
        // The same message re-emitted by a streaming update must count once.
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T01:00:00Z", messageId: "m1", requestId: "r1"),
            AssistantLine("2026-01-10T01:00:05Z", messageId: "m1", requestId: "r1"),
            AssistantLine("2026-01-10T01:00:10Z", messageId: "m2", requestId: "r2"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(2, result.EventCount);
        Assert.Equal(20, result.InputTokens);
    }

    [Fact]
    public void Compute_DeduplicatesAcrossFiles()
    {
        // A resumed session replays the same message into a second file.
        WriteLog("proj/a.jsonl", AssistantLine("2026-01-10T01:00:00Z", messageId: "m1", requestId: "r1"));
        WriteLog("proj/b.jsonl", AssistantLine("2026-01-10T01:00:00Z", messageId: "m1", requestId: "r1"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_RowsWithoutAnyId_AreNotDeduplicated()
    {
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T01:00:00Z", messageId: null, requestId: null),
            AssistantLine("2026-01-10T01:00:05Z", messageId: null, requestId: null));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(2, result.EventCount);
    }

    [Fact]
    public void Compute_UnknownModel_CountedSeparately_NoTokensAdded()
    {
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T01:00:00Z", model: "<synthetic>", messageId: "m1", requestId: "r1"),
            AssistantLine("2026-01-10T01:00:05Z", model: "mystery-model-9", messageId: "m2", requestId: "r2"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.False(result.HasData);
        Assert.Equal(0, result.EventCount);
        Assert.Equal(2, result.UnknownModelEvents);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0m, result.CostUsd);
    }

    [Fact]
    public void Compute_SkipsMalformedLines()
    {
        WriteLog("proj/a.jsonl",
            "{\"input_tokens\": broken json",                       // passes prefilter, fails parse
            "not json at all",                                       // fails prefilter
            AssistantLine("2026-01-10T01:00:00Z"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_SkipsFilesNotModifiedSinceStart()
    {
        var path = WriteLog("proj/a.jsonl", AssistantLine("2026-01-10T01:00:00Z"));
        File.SetLastWriteTimeUtc(path, StartUtc.UtcDateTime.AddDays(-2));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(DailyCostResult.Empty, result);
    }

    [Fact]
    public void Compute_RecursesIntoSubdirectories()
    {
        // Subagent transcripts live in nested directories.
        WriteLog(Path.Combine("proj", "subagents", "agent.jsonl"),
            AssistantLine("2026-01-10T01:00:00Z"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        var result = ClaudeDailyCostReader.Compute(missing, StartUtc, EndUtc);

        Assert.Equal(DailyCostResult.Empty, result);
        Assert.False(result.HasData);
    }

    [Fact]
    public void Compute_ReadsFileOpenedByAnotherWriter()
    {
        // Claude Code keeps the log open for appending while we read it.
        var path = Path.Combine(_root, "live.jsonl");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using (var text = new StreamWriter(writer, leaveOpen: true))
        {
            text.WriteLine(AssistantLine("2026-01-10T01:00:00Z"));
            text.Flush();
        }

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(1, result.EventCount);
    }

    [Fact]
    public void Compute_ReportsPerModelCostBreakdown()
    {
        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-10T01:00:00Z", model: "claude-opus-4-8", messageId: "m1", requestId: "r1",
                input: 1000, output: 1000),
            AssistantLine("2026-01-10T02:00:00Z", model: "claude-opus-4-8", messageId: "m2", requestId: "r2",
                input: 1000, output: 1000),
            AssistantLine("2026-01-10T03:00:00Z", model: "claude-sonnet-5", messageId: "m3", requestId: "r3",
                input: 1000, output: 1000),
            // Unpriced model: no entry in the breakdown, counted separately.
            AssistantLine("2026-01-10T04:00:00Z", model: "mystery-model-9", messageId: "m4", requestId: "r4"));

        var result = ClaudeDailyCostReader.Compute(_root, StartUtc, EndUtc);

        Assert.Equal(2, result.CostByModelUsd.Count);
        // claude-opus-4-8: 2 x (1000*5 + 1000*25) / 1e6.
        Assert.Equal(0.06m, result.CostByModelUsd["claude-opus-4-8"]);
        // claude-sonnet-5: (1000*2 + 1000*10) / 1e6.
        Assert.Equal(0.012m, result.CostByModelUsd["claude-sonnet-5"]);
        Assert.Equal(1, result.UnknownModelEvents);
        // The breakdown always sums to the total.
        Assert.Equal(result.CostUsd, result.CostByModelUsd.Values.Sum());
        Assert.Equal(1, result.UnknownModelEventsById["mystery-model-9"]);
    }

    [Fact]
    public void ComputeMany_SingleScan_KeepsTodayASubsetOfTheMonth()
    {
        var monthStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var intervals = new[]
        {
            new CostInterval(StartUtc, EndUtc),       // "today"
            new CostInterval(monthStart, EndUtc)      // "this month", contains today
        };

        WriteLog("proj/a.jsonl",
            AssistantLine("2026-01-03T01:00:00Z", messageId: "m1", requestId: "r1"),   // month only
            AssistantLine("2026-01-10T01:00:00Z", messageId: "m2", requestId: "r2"),   // today + month
            AssistantLine("2025-12-31T23:00:00Z", messageId: "m3", requestId: "r3"));  // neither

        var results = ClaudeDailyCostReader.ComputeMany(_root, intervals);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].EventCount);
        Assert.Equal(2, results[1].EventCount);
        Assert.True(results[0].CostUsd <= results[1].CostUsd);
        Assert.Equal(10L, results[0].InputTokens);
        Assert.Equal(20L, results[1].InputTokens);
    }

    [Fact]
    public void ComputeMany_DedupIsGlobal_ButCountsOncePerInterval()
    {
        var monthStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var intervals = new[]
        {
            new CostInterval(StartUtc, EndUtc),
            new CostInterval(monthStart, EndUtc)
        };

        // The same replayed event appears in two files: it must be counted
        // once in EACH interval that contains it, not twice in either.
        WriteLog("proj/a.jsonl", AssistantLine("2026-01-10T01:00:00Z", messageId: "m1", requestId: "r1"));
        WriteLog("proj/b.jsonl", AssistantLine("2026-01-10T01:00:00Z", messageId: "m1", requestId: "r1"));

        var results = ClaudeDailyCostReader.ComputeMany(_root, intervals);

        Assert.Equal(1, results[0].EventCount);
        Assert.Equal(1, results[1].EventCount);
    }

    [Fact]
    public void ComputeMany_UsesEarliestStartForTheLastWriteTimePrefilter()
    {
        var monthStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Last written during the month but before "today": the month interval
        // must still see it, so the prefilter has to use the earliest start.
        var path = WriteLog("proj/a.jsonl", AssistantLine("2026-01-03T01:00:00Z"));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 3, 2, 0, 0, DateTimeKind.Utc));

        var results = ClaudeDailyCostReader.ComputeMany(
            _root,
            new[] { new CostInterval(StartUtc, EndUtc), new CostInterval(monthStart, EndUtc) });

        Assert.Equal(0, results[0].EventCount);
        Assert.Equal(1, results[1].EventCount);
    }

    [Fact]
    public void ComputeMany_NoIntervals_ReturnsEmptyList()
        => Assert.Empty(ClaudeDailyCostReader.ComputeMany(_root, Array.Empty<CostInterval>()));
}
