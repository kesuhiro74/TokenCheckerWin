namespace TokenChecker.Core.Providers;

// Pure classification of a Codex app-server account/read result into the
// rate-limit-collection decision, pulled out of CodexUsageProvider so the branch
// logic (login required / unsupported account type / ok-to-proceed) is unit-testable
// without launching the real app-server. Public because Core has no
// InternalsVisibleTo (same convention as GitHubBillingUsageParser).
//
// Messages are fixed strings with no account details, safe for masked diagnostics.
public static class CodexAccountClassifier
{
    public static CodexAccountDecision Classify(bool accountIsNull, string? accountType, bool requiresOpenAiAuth)
    {
        if (accountIsNull && requiresOpenAiAuth)
        {
            return CodexAccountDecision.Fail(ProviderStatus.NotLoggedIn, "Codex login is required.");
        }

        if (accountIsNull)
        {
            return CodexAccountDecision.Fail(
                ProviderStatus.Error,
                "Codex account state does not allow rate limit collection.");
        }

        if (!string.Equals(accountType, "chatgpt", StringComparison.Ordinal))
        {
            var message = string.Equals(accountType, "apiKey", StringComparison.Ordinal)
                ? "ChatGPT login is required for rate limits."
                : "Codex account type does not support rate limit collection.";

            return CodexAccountDecision.Fail(ProviderStatus.Error, message);
        }

        return CodexAccountDecision.Proceed();
    }
}

// CanProceed == true means the account is a usable ChatGPT login; Status/Message are
// only meaningful for the failure case (CanProceed == false).
public readonly record struct CodexAccountDecision(bool CanProceed, ProviderStatus Status, string? Message)
{
    public static CodexAccountDecision Proceed() => new(true, ProviderStatus.Available, null);

    public static CodexAccountDecision Fail(ProviderStatus status, string message) => new(false, status, message);
}
