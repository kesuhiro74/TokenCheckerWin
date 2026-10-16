using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

// Parsed form of a 2xx body from api.anthropic.com/api/oauth/usage.
public sealed record ClaudeUsageParseResult(
    IReadOnlyList<RateLimitWindow> Windows,
    ServiceExtras Extras);

// Pure JSON -> model translation for the Claude usage endpoint, pulled out of
// ClaudeUsageProvider so every branch (new limits[] shape, legacy
// seven_day_opus/sonnet, spend vs extra_usage) can be unit-tested against
// recorded response bodies without a live HTTP round-trip. Public for the same
// reason ClaudeUsageStatusMapper is: Core has no InternalsVisibleTo.
//
// Response shape as observed on 2026-10-16:
//   five_hour / seven_day            : { utilization: 0-100, resets_at: ISO }
//   seven_day_opus / seven_day_sonnet: same shape, usually null (legacy)
//   limits[]                         : { kind: session|weekly_all|weekly_scoped,
//                                        group, percent, severity,
//                                        resets_at: ISO|null, is_active,
//                                        scope: null | { model: {display_name}|null,
//                                                        surface: {...}|null } }
//   spend                            : { used: {amount_minor, currency, exponent},
//                                        limit: same|null, percent, enabled }
//   extra_usage                      : { is_enabled, monthly_limit, used_credits,
//                                        currency }  // amounts in cents
//
// Deliberately ignored: the obfuscated keys the endpoint also returns
// (iguana_necktie and friends) are unstable and never read.
public static class ClaudeUsageResponseParser
{
    private const long FiveHourDurationMins = 300;
    private const long WeeklyDurationMins = 10080;

    private const string FiveHourName = "Claude 5h";
    private const string WeeklyName = "Claude Weekly";

    // subscriptionType / rateLimitTier come from the credentials file (read
    // once per refresh by the provider) and only feed the plan label.
    public static ClaudeUsageParseResult Parse(JsonNode root, string? subscriptionType, string? rateLimitTier)
    {
        ArgumentNullException.ThrowIfNull(root);

        var limits = ReadLimits(root["limits"]);
        var windows = new List<RateLimitWindow>(2);

        // The documented fields stay authoritative; limits[] only fills a gap,
        // as insurance against the endpoint dropping them in a future shape.
        var fiveHour = ParseUtilizationWindow(root["five_hour"], FiveHourName, FiveHourDurationMins)
            ?? ParsePercentWindow(FindLimit(limits, "session"), FiveHourName, FiveHourDurationMins);
        if (fiveHour is not null)
        {
            windows.Add(fiveHour);
        }

        var weekly = ParseUtilizationWindow(root["seven_day"], WeeklyName, WeeklyDurationMins)
            ?? ParsePercentWindow(FindLimit(limits, "weekly_all"), WeeklyName, WeeklyDurationMins);
        if (weekly is not null)
        {
            windows.Add(weekly);
        }

        var extras = new ServiceExtras(
            PlanLabel: ClaudePlanLabel.From(subscriptionType, rateLimitTier),
            ScopedLimits: ParseScopedLimits(limits, root),
            ExtraSpend: ParseExtraSpend(root));

        return new ClaudeUsageParseResult(windows, extras);
    }

    private static IReadOnlyList<JsonNode> ReadLimits(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<JsonNode>();
        }

        var items = new List<JsonNode>(array.Count);
        foreach (var item in array)
        {
            if (!IsJsonNull(item))
            {
                items.Add(item!);
            }
        }

        return items;
    }

    // is_active is intentionally NOT used as a filter: an inactive-but-present
    // limit is still a limit the user is subject to, and silently dropping one
    // would hide a real constraint.
    private static JsonNode? FindLimit(IReadOnlyList<JsonNode> limits, string kind)
    {
        foreach (var limit in limits)
        {
            if (string.Equals(GetString(limit["kind"]), kind, StringComparison.OrdinalIgnoreCase))
            {
                return limit;
            }
        }

        return null;
    }

    private static IReadOnlyList<ScopedLimit>? ParseScopedLimits(IReadOnlyList<JsonNode> limits, JsonNode root)
    {
        var scoped = new List<ScopedLimit>();

        foreach (var limit in limits)
        {
            if (!string.Equals(GetString(limit["kind"]), "weekly_scoped", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // scope.model.id was null in the observed response, so the model
            // display name is the only identifier available; fall back to the
            // surface display name for surface-scoped limits.
            var scope = limit["scope"];
            var label = GetString(scope?["model"]?["display_name"])
                ?? GetString(scope?["surface"]?["display_name"]);

            if (string.IsNullOrWhiteSpace(label))
            {
                // No display name to show it under: skip rather than invent a
                // label the user cannot interpret.
                continue;
            }

            scoped.Add(new ScopedLimit(
                label,
                ClampPercent(GetDouble(limit["percent"])),
                GetDateTimeOffset(limit["resets_at"])));
        }

        if (scoped.Count > 0)
        {
            return scoped;
        }

        // Legacy per-model fields, used only when limits[] carries no scoped
        // entry at all (both were null in the observed response).
        AddLegacyScopedLimit(scoped, root["seven_day_opus"], "Opus");
        AddLegacyScopedLimit(scoped, root["seven_day_sonnet"], "Sonnet");

        return scoped.Count > 0 ? scoped : null;
    }

    private static void AddLegacyScopedLimit(List<ScopedLimit> scoped, JsonNode? node, string label)
    {
        if (node is not JsonObject window)
        {
            return;
        }

        var percent = ClampPercent(GetDouble(window["utilization"]));
        if (percent is null)
        {
            return;
        }

        scoped.Add(new ScopedLimit(label, percent, GetDateTimeOffset(window["resets_at"])));
    }

    private static ExtraSpend? ParseExtraSpend(JsonNode root)
    {
        // Preferred: `spend`, which reports real money in minor units.
        if (root["spend"] is JsonObject spend && GetBool(spend["enabled"]) == true)
        {
            var used = ParseMinorAmount(spend["used"]);
            var limit = ParseMinorAmount(spend["limit"]);
            var currency = GetString(spend["used"]?["currency"])
                ?? GetString(spend["currency"])
                ?? "USD";

            return new ExtraSpend(used ?? 0m, limit, currency);
        }

        // Fallback: the older `extra_usage` block, whose monthly_limit and
        // used_credits are plain cents.
        if (root["extra_usage"] is JsonObject extraUsage && GetBool(extraUsage["is_enabled"]) == true)
        {
            var used = GetDecimal(extraUsage["used_credits"]) ?? 0m;
            var limit = GetDecimal(extraUsage["monthly_limit"]);
            var currency = GetString(extraUsage["currency"]) ?? "USD";

            return new ExtraSpend(used / 100m, limit / 100m, currency);
        }

        return null;
    }

    // { amount_minor, currency, exponent } -> major units. The observed
    // response only ever had an object here for `used`; `limit` was null and
    // its populated shape is unconfirmed, so anything that is not an object
    // carrying amount_minor yields null instead of a wrong number.
    private static decimal? ParseMinorAmount(JsonNode? node)
    {
        if (node is not JsonObject amount)
        {
            return null;
        }

        var minor = GetDecimal(amount["amount_minor"]);
        if (minor is null)
        {
            return null;
        }

        var exponent = GetInt(amount["exponent"]) ?? 2;
        if (exponent is < 0 or > 9)
        {
            exponent = 2;
        }

        decimal divisor = 1m;
        for (var i = 0; i < exponent; i++)
        {
            divisor *= 10m;
        }

        return minor.Value / divisor;
    }

    // five_hour / seven_day shape: utilization is a 0-100 percentage.
    private static RateLimitWindow? ParseUtilizationWindow(JsonNode? node, string name, long durationMins)
    {
        if (node is not JsonObject window)
        {
            return null;
        }

        return BuildWindow(ClampPercent(GetDouble(window["utilization"])), window["resets_at"], name, durationMins);
    }

    // limits[] shape: percent is an integer 0-100.
    private static RateLimitWindow? ParsePercentWindow(JsonNode? node, string name, long durationMins)
    {
        if (node is not JsonObject window)
        {
            return null;
        }

        return BuildWindow(ClampPercent(GetDouble(window["percent"])), window["resets_at"], name, durationMins);
    }

    private static RateLimitWindow? BuildWindow(double? usedPercent, JsonNode? resetsAt, string name, long durationMins)
    {
        if (usedPercent is null)
        {
            return null;
        }

        var used = (long)Math.Round(usedPercent.Value, MidpointRounding.AwayFromZero);
        var remaining = Math.Max(0, 100 - (long)Math.Ceiling(usedPercent.Value));

        return new RateLimitWindow(
            name,
            GetDateTimeOffset(resetsAt),
            used,
            100,
            remaining,
            usedPercent,
            durationMins);
    }

    private static double? ClampPercent(double? value)
        => value is null ? null : Math.Clamp(value.Value, 0, 100);

    private static bool IsJsonNull(JsonNode? node)
        => node is null || node.GetValueKind() == JsonValueKind.Null;

    private static string? GetString(JsonNode? node)
    {
        if (IsJsonNull(node) || node!.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    private static bool? GetBool(JsonNode? node)
    {
        if (IsJsonNull(node))
        {
            return null;
        }

        return node!.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? GetDouble(JsonNode? node)
    {
        if (IsJsonNull(node))
        {
            return null;
        }

        if (node!.GetValueKind() == JsonValueKind.Number)
        {
            try
            {
                return node.GetValue<double>();
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            {
                return null;
            }
        }

        var text = GetString(node);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    // Money is parsed as decimal (never double) so sub-cent values do not
    // drift, matching the ModelPricing convention.
    private static decimal? GetDecimal(JsonNode? node)
    {
        if (IsJsonNull(node))
        {
            return null;
        }

        if (node!.GetValueKind() == JsonValueKind.Number)
        {
            try
            {
                return node.GetValue<decimal>();
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            {
                return null;
            }
        }

        var text = GetString(node);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? GetInt(JsonNode? node)
    {
        var value = GetDecimal(node);
        if (value is null || value.Value > int.MaxValue || value.Value < int.MinValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonNode? node)
    {
        var text = GetString(node);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
