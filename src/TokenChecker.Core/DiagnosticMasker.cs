using System.Text.RegularExpressions;

namespace TokenChecker.Core;

// Single source of truth for masking sensitive substrings out of diagnostic
// text before it is ever surfaced (UI "details", logs, persisted files).
// Privacy is a hard invariant for this app, so both the providers (Core) and
// the UI presenter (App) route through here instead of each carrying their own
// copy of the rules — one place to audit, no chance of the copies drifting.
//
// Masks, in order:
//   email-like                       -> <email>
//   Windows absolute path            -> <path>
//   UNC path (\\server\share\...)    -> <path>
//   POSIX absolute path              -> <path>
//   token/secret/key/...=VALUE       -> name=<redacted>
//   JWT (eyJ........)                -> <redacted>
//   long alphanumeric run (>=32)     -> <redacted>
public static partial class DiagnosticMasker
{
    public static string Mask(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        // A negative budget would make the final slice throw; clamp to 0 (empty).
        if (maxLength < 0)
        {
            maxLength = 0;
        }

        var masked = EmailRegex().Replace(value, "<email>");
        masked = WindowsPathRegex().Replace(masked, "<path>");
        masked = UncPathRegex().Replace(masked, "<path>");
        masked = PosixPathRegex().Replace(masked, "<path>");
        masked = SecretAssignmentRegex().Replace(masked, "$1=<redacted>");
        // JWTs run before the generic long-token rule so the whole token collapses
        // to one <redacted> instead of leaving the (non-secret but identifying)
        // header segment and dot separators behind.
        masked = JwtRegex().Replace(masked, "<redacted>");
        masked = LongTokenRegex().Replace(masked, "<redacted>");

        return masked.Length <= maxLength ? masked : masked[..maxLength];
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\\s]+\\)*[^\\\s]*")]
    private static partial Regex WindowsPathRegex();

    // \\server\share[\...] — a UNC path needs the leading double backslash and at
    // least one component after the host, so a lone "domain\user" is not matched.
    [GeneratedRegex(@"\\\\[^\\\s]+(?:\\[^\\\s]+)+")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"/(?:[^/\s]+/)+[^/\s]*")]
    private static partial Regex PosixPathRegex();

    [GeneratedRegex(@"(?i)(token|secret|key|authorization|bearer)\s*[:=]\s*\S+")]
    private static partial Regex SecretAssignmentRegex();

    // base64url JWT: header.payload[.signature], header always begins "eyJ" ({" ).
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)?")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9_-]{32,}\b")]
    private static partial Regex LongTokenRegex();
}
