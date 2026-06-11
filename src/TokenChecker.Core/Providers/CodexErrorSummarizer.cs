using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

// Builds the safe "code=...; message=..." one-line summary of a JSON-RPC error
// object returned by the Codex app-server, pulled out of CodexUsageProvider so the
// masking/formatting can be unit-tested without launching the real app-server.
// Public because Core has no InternalsVisibleTo (same convention as
// ClaudeUsageStatusMapper / CodexAccountClassifier / GitHubBillingUsageParser).
//
// Privacy: the message is always routed through DiagnosticMasker. The code is only
// emitted verbatim when it is a JSON number (an error code, never sensitive); any
// other kind (string/object/etc.) could carry arbitrary text, so it collapses to
// <redacted> rather than being printed raw.
public static class CodexErrorSummarizer
{
    public static string Summarize(JsonNode? error)
    {
        var code = SummarizeCode(error?["code"]);
        var message = DiagnosticMasker.Mask(
            error?["message"]?.GetValue<string>() ?? "Codex app-server returned an error.",
            maxLength: 160);

        return $"code={code}; message={message}";
    }

    private static string SummarizeCode(JsonNode? codeNode)
    {
        if (codeNode is null)
        {
            return "unknown";
        }

        // Only a JSON number is a safe-to-print error code. ToJsonString() on a
        // string/object would leak its raw contents, so anything non-numeric is
        // redacted instead of emitted verbatim.
        return codeNode.GetValueKind() == JsonValueKind.Number
            ? codeNode.ToJsonString()
            : "<redacted>";
    }
}
