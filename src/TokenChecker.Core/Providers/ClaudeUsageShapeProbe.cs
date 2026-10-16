using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

// Diagnostic-only result for ClaudeUsageProvider.ProbeUsageShapeAsync. It exists
// purely to let TokenChecker.Poc inspect the *shape* of the Claude usage API
// response (key paths and value kinds) without any caller duplicating the
// credential-reading or HTTP logic that ClaudeUsageProvider already owns.
//
// Privacy: this type intentionally carries only what the POC is allowed to
// print. It never carries the access/refresh token or token expiry. OauthKeys
// is a list of key NAMES under claudeAiOauth only (no values); SubscriptionType
// and RateLimitTier are plan-tier enum values the POC may mask and print per
// the investigation instructions. Body is the raw parsed usage response and is
// only populated for a 2xx response — the POC walks it to print a structure
// dump, never the raw body itself.
public sealed record ClaudeUsageShapeProbe(
    bool CredentialsPresent,
    IReadOnlyList<string> OauthKeys,
    string? SubscriptionType,
    string? RateLimitTier,
    bool AccessTokenPresent,
    int? HttpStatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    JsonNode? Body,
    string? ErrorSummary)
{
    public static ClaudeUsageShapeProbe NoCredentials() => new(
        CredentialsPresent: false,
        OauthKeys: Array.Empty<string>(),
        SubscriptionType: null,
        RateLimitTier: null,
        AccessTokenPresent: false,
        HttpStatusCode: null,
        ResponseHeaders: new Dictionary<string, string>(),
        Body: null,
        ErrorSummary: "credentialsMissing");
}
