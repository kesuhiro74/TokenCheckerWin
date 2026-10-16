using System.Text.Json.Nodes;
using TokenChecker.Core;
using TokenChecker.Core.Providers;

namespace TokenChecker.Poc.ClaudeUsage;

// POC runner for --usage-shape. Investigates the actual shape of the Claude
// usage API response (api.anthropic.com/api/oauth/usage) ahead of building
// per-model weekly limits / extra_usage / limits[] parsing, without leaking
// any secret or personally identifying value.
//
// Reuses ClaudeUsageProvider.ProbeUsageShapeAsync for credential reading and
// the HTTP call (same auth, same headers, same URL as production), so this
// file only formats what comes back. Calls the live endpoint at most once.
//
// Privacy rules enforced here (see task instructions):
//   - Credential file: only claudeAiOauth key NAMES are printed. Values are
//     never printed, except subscriptionType/rateLimitTier, which are masked
//     via DiagnosticMasker before printing.
//   - Response headers: only anthropic-ratelimit-* and retry-after, by name
//     and value (these are rate-limit telemetry, not secrets).
//   - Response body: printed as a structure dump, not a raw dump. Numbers,
//     bools and null pass through as-is (explicitly allowed by the task).
//     Strings are replaced with "<string len=N>" UNLESS the key is one of
//     kind/group/severity/currency/display_name/resets_at/status/
//     locked_reason, in which case the value is masked via DiagnosticMasker
//     and printed. Arrays are expanded up to 5 elements.
internal static class ClaudeUsageShapePocRunner
{
    private const int DiagnosticMaxLength = 80;

    private static readonly HashSet<string> AllowedStringKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "group", "severity", "currency", "display_name", "resets_at", "status", "locked_reason"
    };

    public static async Task<int> RunAsync()
    {
        var provider = new ClaudeUsageProvider();
        var probe = await provider.ProbeUsageShapeAsync().ConfigureAwait(false);

        Console.WriteLine("=== claude usage-shape probe ===");

        if (!probe.CredentialsPresent)
        {
            Console.WriteLine("credentialsPresent=false");
            return 0;
        }

        Console.WriteLine("credentialsPresent=true");
        Console.WriteLine($"claudeAiOauth keys: {(probe.OauthKeys.Count == 0 ? "(none)" : string.Join(", ", probe.OauthKeys))}");
        Console.WriteLine($"subscriptionType: {DiagnosticMasker.Mask(probe.SubscriptionType, DiagnosticMaxLength)}");
        Console.WriteLine($"rateLimitTier: {DiagnosticMasker.Mask(probe.RateLimitTier, DiagnosticMaxLength)}");
        Console.WriteLine($"accessTokenPresent={(probe.AccessTokenPresent ? "true" : "false")}");

        if (!probe.AccessTokenPresent)
        {
            Console.WriteLine($"result: {probe.ErrorSummary}");
            return 0;
        }

        if (probe.HttpStatusCode is null)
        {
            Console.WriteLine($"result: {probe.ErrorSummary}");
            return 0;
        }

        Console.WriteLine($"httpStatusCode: {probe.HttpStatusCode}");
        PrintHeaders(probe.ResponseHeaders);

        if (probe.HttpStatusCode is < 200 or > 299)
        {
            // Error status: per the task, report status + headers only, no
            // retry, no body parsing.
            Console.WriteLine("result: non-success status, stopping without retry.");
            return 0;
        }

        if (probe.Body is null)
        {
            Console.WriteLine("result: 2xx but body did not parse as JSON.");
            return 0;
        }

        Console.WriteLine("--- response body structure ---");
        foreach (var line in DumpStructure(probe.Body))
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    private static void PrintHeaders(IReadOnlyDictionary<string, string> headers)
    {
        Console.WriteLine("--- response headers (anthropic-ratelimit-*, retry-after) ---");
        if (headers.Count == 0)
        {
            Console.WriteLine("(none present)");
            return;
        }

        foreach (var (name, value) in headers.OrderBy(static h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{name}: {value}");
        }
    }

    private static IEnumerable<string> DumpStructure(JsonNode node)
    {
        var lines = new List<string>();
        Dump(string.Empty, node, lines);
        return lines;
    }

    private static void Dump(string path, JsonNode? node, List<string> lines)
    {
        if (node is null)
        {
            lines.Add($"{DisplayPath(path)}: null");
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var childPath = path.Length == 0 ? key : $"{path}.{key}";
                    Dump(childPath, value, lines);
                }
                break;

            case JsonArray array:
                lines.Add($"{DisplayPath(path)}: array len={array.Count}");
                var limit = Math.Min(array.Count, 5);
                for (var i = 0; i < limit; i++)
                {
                    Dump($"{path}[{i}]", array[i], lines);
                }
                break;

            case JsonValue value:
                DumpValue(path, value, lines);
                break;
        }
    }

    private static void DumpValue(string path, JsonValue value, List<string> lines)
    {
        var displayPath = DisplayPath(path);
        System.Text.Json.JsonElement element;
        try
        {
            element = value.GetValue<System.Text.Json.JsonElement>();
        }
        catch
        {
            lines.Add($"{displayPath}: <unreadable>");
            return;
        }

        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Number:
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                lines.Add($"{displayPath}: {element.GetRawText()}");
                break;

            case System.Text.Json.JsonValueKind.Null:
                lines.Add($"{displayPath}: null");
                break;

            case System.Text.Json.JsonValueKind.String:
                var key = LeafKey(path);
                var text = element.GetString() ?? string.Empty;
                lines.Add(AllowedStringKeys.Contains(key)
                    ? $"{displayPath}: {DiagnosticMasker.Mask(text, DiagnosticMaxLength)}"
                    : $"{displayPath}: <string len={text.Length}>");
                break;

            default:
                lines.Add($"{displayPath}: <unknown kind={element.ValueKind}>");
                break;
        }
    }

    private static string DisplayPath(string path) => path.Length == 0 ? "(root)" : path;

    private static string LeafKey(string path)
    {
        var lastDot = path.LastIndexOf('.');
        var segment = lastDot >= 0 ? path[(lastDot + 1)..] : path;
        var bracket = segment.IndexOf('[');
        return bracket >= 0 ? segment[..bracket] : segment;
    }
}
