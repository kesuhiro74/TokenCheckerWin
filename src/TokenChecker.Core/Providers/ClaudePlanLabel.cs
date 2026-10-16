namespace TokenChecker.Core.Providers;

// Pure mapping from the two plan-tier fields of ~/.claude/.credentials.json
// (claudeAiOauth.subscriptionType and claudeAiOauth.rateLimitTier) to a short
// display label such as "Max 5x". Public and side-effect free so it can be
// unit-tested without touching a credentials file (Core has no
// InternalsVisibleTo), same rationale as ClaudeUsageStatusMapper.
//
// Privacy: both inputs are plan-tier enum values, never secrets, and the
// output is a display label only. Nothing else from the credentials file is
// read by the caller.
public static class ClaudePlanLabel
{
    // Returns null for unknown/missing tiers so the UI can simply omit the
    // plan line instead of showing a raw API value.
    public static string? From(string? subscriptionType, string? rateLimitTier)
    {
        // The tier is the more specific of the two (it carries the Max
        // multiplier), so it wins when recognizable.
        if (!string.IsNullOrWhiteSpace(rateLimitTier))
        {
            if (rateLimitTier.Contains("max_5x", StringComparison.OrdinalIgnoreCase))
            {
                return "Max 5x";
            }

            if (rateLimitTier.Contains("max_20x", StringComparison.OrdinalIgnoreCase))
            {
                return "Max 20x";
            }
        }

        if (string.IsNullOrWhiteSpace(subscriptionType))
        {
            return null;
        }

        return subscriptionType.Trim().ToLowerInvariant() switch
        {
            "max" => "Max",
            "pro" => "Pro",
            "team" => "Team",
            "enterprise" => "Enterprise",
            "free" => "Free",
            _ => null
        };
    }
}
