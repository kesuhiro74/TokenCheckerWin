using System.Globalization;

namespace TokenChecker.Core.Providers;

// Pure parser for the Retry-After response header on a 429 from the Claude
// usage endpoint, so ClaudeUsageProvider can stop calling the endpoint until
// the server says it is worth retrying. Public and side-effect free so it can
// be unit-tested without a live HTTP round-trip (Core has no
// InternalsVisibleTo), same rationale as ClaudeUsageStatusMapper.
//
// The resulting cooldown is held in memory by the provider only; it is never
// persisted.
public static class ClaudeRetryAfter
{
    // Used when the header is absent or unparseable: long enough to stop the
    // tray app's refresh timer from hammering a limited endpoint, short enough
    // that usage reappears soon after the limit clears.
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    // Hard ceiling: a hostile or mistaken header must not silence usage
    // reporting for hours.
    public static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(60);

    // Accepts both Retry-After forms (RFC 9110): delta-seconds, or an HTTP
    // date. A value of zero or in the past means "retry now", so it yields
    // TimeSpan.Zero (no cooldown) rather than the default.
    public static TimeSpan Parse(string? headerValue, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return DefaultCooldown;
        }

        var text = headerValue.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds <= 0
                ? TimeSpan.Zero
                : Clamp(TimeSpan.FromSeconds(Math.Min(seconds, (long)MaxCooldown.TotalSeconds)));
        }

        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var retryAt))
        {
            return Clamp(retryAt - nowUtc);
        }

        return DefaultCooldown;
    }

    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > MaxCooldown ? MaxCooldown : value;
    }
}
