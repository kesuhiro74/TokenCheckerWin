using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.LocalCost;

// Aggregates today's token usage from the local Claude Code session logs
// (~/.claude/projects/**/*.jsonl) and prices it with ModelPricing.
//
// Privacy invariant: only numeric token counts, model ids (used transiently
// for pricing lookups), and message/request ids (used transiently for dedup)
// are ever read into variables; of these, only the numeric totals are
// returned. Conversation content, cwd values, and file paths are never
// captured or returned.
public static class ClaudeDailyCostReader
{
    // Mirrors the config-dir resolution in ClaudeUsageProvider: CLAUDE_CONFIG_DIR
    // overrides the default ~/.claude.
    public static string ResolveProjectsDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configDirectory = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        return Path.Combine(configDirectory, "projects");
    }

    // Sums assistant-message usage events whose timestamp falls in the UTC
    // half-open interval [startUtc, endUtc).
    public static DailyCostResult Compute(string projectsDirectory, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (!Directory.Exists(projectsDirectory))
        {
            return DailyCostResult.Empty;
        }

        decimal costUsd = 0m;
        long inputTokens = 0;
        long outputTokens = 0;
        long cacheWriteTokens = 0;
        long cacheReadTokens = 0;
        var eventCount = 0;
        var unknownModelEvents = 0;

        // Dedup keys span all files: resumed sessions replay earlier assistant
        // messages into a new .jsonl, so the same message can appear twice.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        // Recursion is required: subagent transcripts live in subdirectories.
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectsDirectory, "*.jsonl", SearchOption.AllDirectories);
        }
        catch
        {
            return DailyCostResult.Empty;
        }

        foreach (var file in files)
        {
            try
            {
                // Files not touched since the interval started cannot contain
                // in-range events (logs are append-only).
                if (File.GetLastWriteTimeUtc(file) < startUtc.UtcDateTime)
                {
                    continue;
                }

                // Claude Code may still be appending to the file; open with a
                // permissive share so an active session does not break the scan
                // (File.ReadLines would throw a sharing violation).
                using var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                while (reader.ReadLine() is { } line)
                {
                    // Cheap prefilter: only assistant usage rows carry this key.
                    if (!line.Contains("\"input_tokens\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        var root = JsonNode.Parse(line);
                        if (root is null
                            || !string.Equals(GetString(root["type"]), "assistant", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!TryGetTimestamp(root["timestamp"], out var timestamp)
                            || timestamp < startUtc
                            || timestamp >= endUtc)
                        {
                            continue;
                        }

                        var message = root["message"];
                        var usage = message?["usage"];
                        if (usage is null)
                        {
                            continue;
                        }

                        var model = GetString(message?["model"]);

                        // Streaming updates re-emit the same assistant message;
                        // dedup on message id + request id. Rows with neither id
                        // are counted as-is (no key to dedup on).
                        var messageId = GetString(message?["id"]);
                        var requestId = GetString(root["requestId"]);
                        if (messageId is not null || requestId is not null)
                        {
                            var key = $"{messageId}:{requestId}";
                            if (!seenKeys.Add(key))
                            {
                                continue;
                            }
                        }

                        var price = ModelPricing.Find(model);
                        if (price is null)
                        {
                            unknownModelEvents++;
                            continue;
                        }

                        // Claude semantics: input_tokens is already uncached;
                        // cache writes/reads are reported separately.
                        var input = GetLong(usage["input_tokens"]);
                        var cacheWrite = GetLong(usage["cache_creation_input_tokens"]);
                        var cacheRead = GetLong(usage["cache_read_input_tokens"]);
                        var output = GetLong(usage["output_tokens"]);

                        eventCount++;
                        inputTokens += input;
                        outputTokens += output;
                        cacheWriteTokens += cacheWrite;
                        cacheReadTokens += cacheRead;
                        costUsd += ModelPricing.Cost(price, input, output, cacheWrite, cacheRead);
                    }
                    catch (JsonException)
                    {
                        // Malformed or non-JSON line: skip it.
                    }
                    catch (InvalidOperationException)
                    {
                        // Unexpected node shape (e.g. object where a value was
                        // expected): skip the line.
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
            costUsd, inputTokens, outputTokens, cacheWriteTokens, cacheReadTokens,
            eventCount, unknownModelEvents);
    }

    private static bool TryGetTimestamp(JsonNode? node, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var text = GetString(node);
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
