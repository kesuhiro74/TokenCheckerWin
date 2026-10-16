namespace TokenChecker.Core.Providers;

// Pure mapping from the Codex app-server planType enum (account/read and the
// rate-limit snapshot both report it) to a short display label. Public and
// side-effect free so it can be unit-tested without launching an app-server
// (Core has no InternalsVisibleTo), same rationale as CodexAccountClassifier.
//
// Privacy: planType is a plan enum, not an identity. Nothing else from
// account/read (email in particular) is read anywhere in this provider.
public static class CodexPlanLabel
{
    // Enum values as of codex-cli 0.156.1. Unrecognized values (including the
    // literal "unknown") return null so the UI omits the plan line rather than
    // printing a raw API token.
    public static string? From(string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType))
        {
            return null;
        }

        return planType.Trim().ToLowerInvariant() switch
        {
            "free" => "Free",
            "go" => "Go",
            "plus" => "Plus",
            "pro" => "Pro",
            "prolite" => "Pro Lite",
            "team" => "Team",
            "self_serve_business_prolite" => "Business",
            "self_serve_business_usage_based" => "Business",
            "business" => "Business",
            "ent26" => "Enterprise",
            "enterprise" => "Enterprise",
            "enterprise_cbp_automation" => "Enterprise",
            "enterprise_cbp_usage_based" => "Enterprise",
            "edu" => "Edu",
            "edu_plus" => "Edu Plus",
            "edu_pro" => "Edu Pro",
            _ => null
        };
    }
}
