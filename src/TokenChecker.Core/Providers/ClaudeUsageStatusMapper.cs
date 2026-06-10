using System.Net;

namespace TokenChecker.Core.Providers;

// Pure HTTP-status -> ProviderStatus classification for the Claude usage endpoint,
// pulled out of ClaudeUsageProvider so the mapping (401/403 -> Unauthorized,
// 429 -> RateLimited, 5xx -> Error, other non-2xx -> Error, 2xx -> proceed) can be
// unit-tested without a live HTTP round-trip. Public for the same reason
// GitHubBillingUsageParser is: Core has no InternalsVisibleTo, so testable helpers
// are public.
//
// The summaries are fixed tokens (never response bodies), so they are safe to
// surface inside masked diagnostics.
public static class ClaudeUsageStatusMapper
{
    // Returns the failure classification for a non-success response, or null when
    // the status is a success (2xx) that should proceed to body parsing.
    public static ClaudeHttpFailure? Classify(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ClaudeHttpFailure(ProviderStatus.Unauthorized, "unauthorized");
        }

        if (code == 429)
        {
            return new ClaudeHttpFailure(ProviderStatus.RateLimited, "rateLimited");
        }

        if (code >= 500)
        {
            return new ClaudeHttpFailure(ProviderStatus.Error, "serverError");
        }

        if (code is >= 200 and <= 299)
        {
            return null;
        }

        return new ClaudeHttpFailure(ProviderStatus.Error, "httpError");
    }
}

public readonly record struct ClaudeHttpFailure(ProviderStatus Status, string SafeSummary);
