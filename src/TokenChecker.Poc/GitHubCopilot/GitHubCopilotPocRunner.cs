using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TokenChecker.Core;
using TokenChecker.Core.Providers.GitHubCopilot;

namespace TokenChecker.Poc.GitHubCopilot;

// POC runner for the GitHub Copilot AI Credits provider. Invoked only when the
// POC is started with --github-copilot, so the default POC output is unchanged.
//
// Three modes:
//   --github-copilot          structured: run the provider through the same
//                             UsageAggregator + JSON options the default POC uses.
//   --github-copilot --internal-probe
//                             ONE GET to the UNOFFICIAL, undocumented
//                             https://api.github.com/copilot_internal/user, to
//                             find out whether a plain fine-grained PAT is
//                             accepted there at all (community notes say it
//                             normally needs a Copilot-app OAuth token). Sent
//                             with the SAME standard headers the provider uses
//                             — no editor/client spoofing (no Editor-Version
//                             etc.). The provider now reads this endpoint too
//                             (best-effort, for the plan name + AI-credit
//                             allowance); this probe stays the schema-discovery
//                             view of it and prints strictly more fields than
//                             the provider consumes.
//   --github-copilot --raw    raw probe: hit the candidate billing endpoints and
//                             print ONLY the status, a couple of safe headers, and
//                             the whitelisted, masked product/sku/model/unitType
//                             plus the numeric quantity/grossQuantity/grossAmount/
//                             discountQuantity/netQuantity/netAmount and the copilot
//                             classification of each usageItem, plus the per-model
//                             credit split and billed overage the card will show,
//                             so the real (post-2026-06-01 AI Credits) schema is
//                             visible without leaking PII.
//
// Privacy: the token is never printed; the resolved login is never printed (URLs
// are shown with {login} substituted); response BODIES are never printed (they can
// contain the login, repositoryName, emails, or URLs). The whitelisted string
// fields (product/sku/unitType) are additionally passed through
// DiagnosticMasker.Mask so even an unexpected PII-shaped value cannot leak; null
// fields are preserved as "(null)" so "field absent" stays distinguishable from
// "empty string" when reading the schema.
internal static class GitHubCopilotPocRunner
{
    private const string ApiBaseUrl = "https://api.github.com";
    // UNOFFICIAL / undocumented, reachable only through --internal-probe.
    private const string InternalUserEndpoint = ApiBaseUrl + "/copilot_internal/user";
    private const string ApiVersion = "2026-03-10";
    // No version suffix: GitHub only requires a User-Agent to be present (kept in
    // sync with GitHubCopilotUsageProvider so a release bump can't make them drift).
    private const string UserAgent = "TokenCheckerWin";
    private const string AcceptHeader = "application/vnd.github+json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--internal-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunInternalProbeAsync().ConfigureAwait(false);
        }

        var raw = args.Any(a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase));
        return raw ? await RunRawAsync().ConfigureAwait(false) : await RunStructuredAsync().ConfigureAwait(false);
    }

    // Single GET to the unofficial /copilot_internal/user endpoint.
    //
    // Output is a STRICT allowlist, printed by PrintInternalUser: the HTTP
    // status, x-ratelimit-remaining, the TOP-LEVEL KEY NAMES (names only), the
    // copilot_plan / quota_reset_date[_utc] values, and for each child of
    // quota_snapshots its key name plus seven numeric/bool fields. Nothing else
    // is printed — no login, email, ids, tracking ids, org lists, endpoints or
    // tokens — and on a non-200 only the status is printed, never a body.
    private static async Task<int> RunInternalProbeAsync()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("GITHUB_TOKEN is not set; cannot perform the internal probe. (no token is read or printed)");
            return 0;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var probe = await ProbeAsync(InternalUserEndpoint, token, cts.Token).ConfigureAwait(false);

        Console.WriteLine("=== GET /copilot_internal/user (unofficial, probe only) ===");
        if (probe.Note is not null)
        {
            Console.WriteLine($"  request failed: {probe.Note}");
            return 0;
        }

        Console.WriteLine($"  status: {probe.Code}");
        Console.WriteLine($"  x-ratelimit-remaining: {probe.RateLimitRemaining ?? "(none)"}");

        if (probe.Code != 200)
        {
            // Deliberately no body, not even a length: a failure body can carry
            // identifying text.
            Console.WriteLine("  (non-200: response body intentionally not printed)");
            return 0;
        }

        PrintInternalUser(SafeParse(probe.Body));
        return 0;
    }

    private static void PrintInternalUser(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            Console.WriteLine("  body: (absent or not a JSON object)");
            return;
        }

        // Schema discovery: NAMES only, so an unexpected field's value cannot
        // leak just because it exists.
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pair in obj)
        {
            keys.Add(pair.Key);
        }

        Console.WriteLine($"  topLevelKeys: [{string.Join(", ", keys)}]");

        // The only three string values on the allowlist; masked as well, so even
        // an unexpected PII-shaped value cannot get through.
        Console.WriteLine($"  copilot_plan: {StringOnly(obj["copilot_plan"])}");
        Console.WriteLine($"  quota_reset_date: {StringOnly(obj["quota_reset_date"])}");
        Console.WriteLine($"  quota_reset_date_utc: {StringOnly(obj["quota_reset_date_utc"])}");

        if (obj["quota_snapshots"] is not JsonObject snapshots)
        {
            Console.WriteLine("  quota_snapshots: (absent or not an object)");
            return;
        }

        Console.WriteLine("  quota_snapshots:");
        foreach (var pair in snapshots)
        {
            // The child's KEY (e.g. premium_interactions / chat / completions)
            // plus its numeric/bool fields only.
            Console.WriteLine($"    {pair.Key}:");
            foreach (var field in QuotaSnapshotFields)
            {
                Console.WriteLine($"      {field}={NumberOrBool(pair.Value?[field])}");
            }
        }
    }

    // The exact set of quota_snapshots child fields allowed in the output.
    private static readonly string[] QuotaSnapshotFields =
    {
        "entitlement",
        "remaining",
        "quota_remaining",
        "percent_remaining",
        "unlimited",
        "overage_count",
        "overage_permitted"
    };

    // Prints a value ONLY when it is a JSON string (masked). An object/array
    // under one of these names is reported by kind, never by value, so a
    // nested payload cannot be dumped just because it took an allowlisted name.
    private static string StringOnly(JsonNode? node)
    {
        if (node is null)
        {
            return "(absent)";
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String => MaskField(node.GetValue<string>()),
            JsonValueKind.Null => "(null)",
            _ => "(non-string)"
        };
    }

    // Prints a value ONLY when it is a JSON number or boolean; anything else is
    // reported by kind, never by value, so a string/object/array field that
    // happens to share one of these names cannot leak.
    private static string NumberOrBool(JsonNode? node)
    {
        if (node is null)
        {
            return "(absent)";
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.Number => node.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "(null)",
            _ => "(non-numeric)"
        };
    }

    private static async Task<int> RunStructuredAsync()
    {
        var aggregator = new UsageAggregator(new IUsageProvider[] { new GitHubCopilotUsageProvider() });
        var snapshot = await aggregator.CaptureAsync().ConfigureAwait(false);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());

        Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
        return 0;
    }

    private static async Task<int> RunRawAsync()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("GITHUB_TOKEN is not set; cannot perform the raw probe. (no token is read or printed)");
            return 0;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        var userProbe = await ProbeAsync($"{ApiBaseUrl}/user", token, cts.Token).ConfigureAwait(false);
        PrintProbe("GET /user", userProbe);

        if (!GitHubBillingUsageParser.TryGetLogin(SafeParse(userProbe.Body), out var login))
        {
            Console.WriteLine("loginResolved=false; skipping billing probes.");
            return 0;
        }

        Console.WriteLine("loginResolved=true (login value withheld)");

        var encoded = Uri.EscapeDataString(login);
        var now = DateTimeOffset.UtcNow;
        var endpoints = new[]
        {
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/ai_credit/usage?year={now.Year}&month={now.Month}",
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/usage?year={now.Year}&month={now.Month}",
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/premium_request/usage?year={now.Year}&month={now.Month}"
        };

        foreach (var url in endpoints)
        {
            var probe = await ProbeAsync(url, token, cts.Token).ConfigureAwait(false);
            PrintProbe(Label(url, encoded, login), probe);

            PrintUsageSchema(SafeParse(probe.Body));
        }

        return 0;
    }

    private static async Task<RawProbe> ProbeAsync(string url, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd(AcceptHeader);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // In raw mode we read the body even on failure so the usageItems shape
            // is visible; the body itself is never printed (see PrintProbe).
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new RawProbe(
                (int)response.StatusCode,
                body,
                FirstHeader(response, "x-ratelimit-remaining"),
                FirstHeader(response, "x-github-api-version"),
                Note: null);
        }
        catch (Exception ex)
        {
            return new RawProbe(0, null, null, null, DiagnosticMasker.Mask(ex.Message, 200));
        }
    }

    private static void PrintProbe(string label, RawProbe probe)
    {
        Console.WriteLine($"=== {label} ===");
        if (probe.Note is not null)
        {
            Console.WriteLine($"  request failed: {probe.Note}");
            return;
        }

        Console.WriteLine($"  status: {probe.Code}");
        Console.WriteLine($"  x-ratelimit-remaining: {probe.RateLimitRemaining ?? "(none)"}");
        Console.WriteLine($"  x-github-api-version: {probe.ApiVersionHeader ?? "(none)"}");
        // The response body is intentionally NOT printed: it can contain the login,
        // repositoryName, emails, or URLs. Only the whitelisted usageItems fields
        // are surfaced (see PrintItems).
    }

    // Diagnostic schema dump for the billing usage response. Prints the DISTINCT
    // property-name set across all usageItems (schema discovery — names only, so
    // repositoryName/organizationName *values* never leak) and, per row, the
    // `date` (not PII — the field needed to tell a pre-plan-change row from a
    // post-change one) plus the masked product/sku/unitType, the numbers, and the
    // AI-credit classification + credit value. This is what reveals how to scope
    // the total to the current billing period after a mid-month plan change.
    private static void PrintUsageSchema(JsonNode? root)
    {
        if (root?["usageItems"] is not JsonArray array)
        {
            Console.WriteLine("  usageItems: (absent or not an array)");
            return;
        }

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in array)
        {
            if (node is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    keys.Add(pair.Key);
                }
            }
        }

        Console.WriteLine($"  usageItems: {array.Count}; keys: [{string.Join(", ", keys)}]");

        decimal copilotTotal = 0m;
        var copilotItems = new List<GitHubBillingUsageParser.GitHubBillingUsageItem>();
        foreach (var node in array)
        {
            if (node is not JsonObject)
            {
                continue;
            }

            var item = new GitHubBillingUsageParser.GitHubBillingUsageItem(
                Str(node["product"]), Str(node["sku"]), Str(node["unitType"]),
                Dec(node["quantity"]), Dec(node["grossQuantity"]), Dec(node["grossAmount"]),
                Dec(node["netQuantity"]), Dec(node["netAmount"]),
                Str(node["model"]), Dec(node["discountQuantity"]), Dec(node["discountAmount"]));

            var isCopilot = GitHubBillingUsageParser.IsCopilotAiCreditUsage(item);
            var credits = GitHubBillingUsageParser.CreditsForRow(item);
            if (isCopilot)
            {
                copilotTotal += credits;
                copilotItems.Add(item);
            }

            Console.WriteLine(
                $"    date={Str(node["date"]) ?? "(null)"}; "
                + $"product={MaskField(item.Product)}; "
                + $"sku={MaskField(item.Sku)}; "
                // model is a vendor model DISPLAY name, masked like the other
                // whitelisted strings so an unexpected PII-shaped value cannot leak.
                + $"model={MaskField(item.Model)}; "
                + $"unitType={MaskField(item.UnitType)}; "
                + $"grossQuantity={Format(item.GrossQuantity)}; "
                + $"grossAmount={Format(item.GrossAmount)}; "
                + $"discountQuantity={Format(item.DiscountQuantity)}; "
                + $"netAmount={Format(item.NetAmount)}; "
                + $"copilot={isCopilot}; "
                + $"credits={Format(credits)}");
        }

        Console.WriteLine($"  copilot AI-credit total (all rows this calendar month): {Format(copilotTotal)}");

        // What the card will actually show: the per-model credit split and the
        // billed overage, straight out of the shared BuildExtras helper, so the
        // real response can be checked against the UI without running the app.
        var extras = GitHubBillingUsageParser.BuildExtras(copilotItems);
        if (extras?.ModelCredits is { Count: > 0 } models)
        {
            Console.WriteLine("  per-model credits (BuildExtras):");
            foreach (var model in models)
            {
                Console.WriteLine($"    model={MaskField(model.Label)}; credits={Format(model.Credits)}");
            }
        }
        else
        {
            Console.WriteLine("  per-model credits (BuildExtras): (no row carried a model)");
        }

        Console.WriteLine(extras?.ExtraSpend is { } spend
            ? $"  extra spend (BuildExtras): {Format(spend.Used)} {spend.Unit}"
            : "  extra spend (BuildExtras): (no row carried netAmount)");
    }

    private static string? Str(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToString();
    }

    private static decimal? Dec(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        var text = node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToString();
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    // Mask the whitelisted string fields too. null/unset is kept as "(null)" (not
    // passed through Mask, which would flatten it to ""), so a missing field stays
    // distinguishable from an empty string when reading the schema.
    private static string MaskField(string? value)
        => value is null ? "(null)" : DiagnosticMasker.Mask(value, 120);

    private static string Format(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "(null)";

    private static string Label(string url, string encodedLogin, string login)
        => url.Replace(encodedLogin, "{login}", StringComparison.Ordinal)
              .Replace(login, "{login}", StringComparison.Ordinal);

    private static string? FirstHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static JsonNode? SafeParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RawProbe(
        int Code,
        string? Body,
        string? RateLimitRemaining,
        string? ApiVersionHeader,
        string? Note);
}
