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
// Two modes:
//   --github-copilot          structured: run the provider through the same
//                             UsageAggregator + JSON options the default POC uses.
//   --github-copilot --raw    raw probe: hit the candidate billing endpoints and
//                             print ONLY the status, a couple of safe headers, and
//                             the whitelisted, masked product/sku/unitType plus the
//                             numeric quantity/grossQuantity/grossAmount/
//                             netQuantity/netAmount and the copilot classification
//                             of each usageItem, so the real (post-2026-06-01 AI
//                             Credits) schema is visible without leaking PII.
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
        var raw = args.Any(a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase));
        return raw ? await RunRawAsync().ConfigureAwait(false) : await RunStructuredAsync().ConfigureAwait(false);
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

            var items = GitHubBillingUsageParser.ParseUsageItems(SafeParse(probe.Body));
            PrintItems(items);
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

    private static void PrintItems(IReadOnlyList<GitHubBillingUsageParser.GitHubBillingUsageItem> items)
    {
        Console.WriteLine($"  parsed usageItems: {items.Count}");
        foreach (var item in items)
        {
            Console.WriteLine(
                $"    product={MaskField(item.Product)}; "
                + $"sku={MaskField(item.Sku)}; "
                + $"unitType={MaskField(item.UnitType)}; "
                + $"quantity={Format(item.Quantity)}; "
                + $"grossQuantity={Format(item.GrossQuantity)}; "
                + $"grossAmount={Format(item.GrossAmount)}; "
                + $"netQuantity={Format(item.NetQuantity)}; "
                + $"netAmount={Format(item.NetAmount)}; "
                + $"copilot={GitHubBillingUsageParser.IsCopilotAiCreditUsage(item)}");
        }
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
