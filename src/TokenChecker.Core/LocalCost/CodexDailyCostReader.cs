using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.LocalCost;

// Aggregates today's token usage from the local Codex CLI session rollouts
// (~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl) and prices it with
// ModelPricing.
//
// Privacy invariant: only numeric token counts and model ids are ever read
// into variables. Conversation content, cwd values, and file paths are never
// captured or returned.
public static class CodexDailyCostReader
{
    // Codex uses this model when a rollout carries no turn_context line.
    private const string DefaultModel = "gpt-5";

    // CODEX_HOME overrides the default ~/.codex (same convention as the Codex
    // usage provider's CLI).
    public static string ResolveSessionsDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        var codexHome = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        return Path.Combine(codexHome, "sessions");
    }

    // Sums token_count events whose timestamp falls in the UTC half-open
    // interval [startUtc, endUtc).
    public static DailyCostResult Compute(string sessionsDirectory, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (!Directory.Exists(sessionsDirectory))
        {
            return DailyCostResult.Empty;
        }

        decimal costUsd = 0m;
        long inputTokens = 0;
        long outputTokens = 0;
        long cacheReadTokens = 0;
        var eventCount = 0;
        var unknownModelEvents = 0;

        // Dedup keys span all files: resuming a session replays earlier events
        // into a new rollout file.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateCandidateFiles(sessionsDirectory, startUtc, endUtc))
        {
            try
            {
                // Files not touched since the interval started cannot contain
                // in-range events (rollouts are append-only).
                if (File.GetLastWriteTimeUtc(file) < startUtc.UtcDateTime)
                {
                    continue;
                }

                // Codex may still be appending to the file; open with a
                // permissive share so an active session does not break the scan.
                using var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                // The active model is tracked per file and reset at each file
                // boundary; turn_context lines switch it mid-file.
                var currentModel = DefaultModel;

                while (reader.ReadLine() is { } line)
                {
                    // Cheap prefilter: only the two row kinds we care about.
                    if (!line.Contains("token_count", StringComparison.Ordinal)
                        && !line.Contains("turn_context", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        var root = JsonNode.Parse(line);
                        if (root is null)
                        {
                            continue;
                        }

                        var type = GetString(root["type"]);
                        if (string.Equals(type, "turn_context", StringComparison.Ordinal))
                        {
                            // Defensive: ignore turn_context rows without a
                            // payload or model.
                            var model = GetString(root["payload"]?["model"]);
                            if (!string.IsNullOrWhiteSpace(model))
                            {
                                currentModel = model;
                            }

                            continue;
                        }

                        if (!string.Equals(type, "event_msg", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var payload = root["payload"];
                        if (!string.Equals(GetString(payload?["type"]), "token_count", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // token_count rows with a null info payload do occur;
                        // they carry no usage to add.
                        var lastUsage = payload?["info"]?["last_token_usage"];
                        if (lastUsage is null)
                        {
                            continue;
                        }

                        var timestampText = GetString(root["timestamp"]);
                        if (!TryGetTimestamp(timestampText, out var timestamp)
                            || timestamp < startUtc
                            || timestamp >= endUtc)
                        {
                            continue;
                        }

                        var input = GetLong(lastUsage["input_tokens"]);
                        var cached = GetLong(lastUsage["cached_input_tokens"]);
                        var output = GetLong(lastUsage["output_tokens"]);
                        var total = GetLong(lastUsage["total_tokens"]);

                        // Session resume replays events verbatim, so the
                        // timestamp + total pair identifies an event.
                        if (!seenKeys.Add($"{timestampText}:{total}"))
                        {
                            continue;
                        }

                        var price = ModelPricing.Find(currentModel);
                        if (price is null)
                        {
                            unknownModelEvents++;
                            continue;
                        }

                        // Codex semantics: input_tokens includes the cached
                        // part, so split it; cache writes are not billed
                        // separately. reasoning_output_tokens is already
                        // included in output_tokens, so it is NOT added.
                        var uncached = Math.Max(0, input - cached);

                        eventCount++;
                        inputTokens += uncached;
                        cacheReadTokens += cached;
                        outputTokens += output;
                        costUsd += ModelPricing.Cost(price, uncached, output, 0, cached);
                    }
                    catch (JsonException)
                    {
                        // Malformed or non-JSON line: skip it.
                    }
                    catch (InvalidOperationException)
                    {
                        // Unexpected node shape: skip the line.
                    }
                }
            }
            catch
            {
                // File disappeared, locked exclusively, unreadable, etc.:
                // move on to the next file.
            }
        }

        return new DailyCostResult(
            costUsd, inputTokens, outputTokens, CacheWriteTokens: 0, cacheReadTokens,
            eventCount, unknownModelEvents);
    }

    // Rollouts are sharded as sessions/YYYY/MM/DD/rollout-*.jsonl. The day
    // directory name is timezone-ambiguous relative to our UTC interval, so
    // scan one extra day on each side and let the per-event timestamp filter
    // decide.
    private static IEnumerable<string> EnumerateCandidateFiles(
        string sessionsDirectory,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var firstDay = startUtc.Date.AddDays(-1);
        var lastDay = endUtc.Date.AddDays(1);

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var dayDirectory = Path.Combine(
                sessionsDirectory,
                day.Year.ToString("D4", CultureInfo.InvariantCulture),
                day.Month.ToString("D2", CultureInfo.InvariantCulture),
                day.Day.ToString("D2", CultureInfo.InvariantCulture));

            if (!Directory.Exists(dayDirectory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dayDirectory, "rollout-*.jsonl", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static bool TryGetTimestamp(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return text is not null
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    private static long GetLong(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return 0;
        }

        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            return 0;
        }
    }
}
