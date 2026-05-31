using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TokenChecker.Core;
using TokenChecker.Core.Providers.GitHubCopilot;

namespace TokenChecker.Poc.GitHubCopilot;

// EXPERIMENTAL POC runner for the GitHub Copilot provider. Invoked only when the
// POC is started with --github-copilot, so the default POC output is unchanged.
//
// Two modes:
//   --github-copilot          structured: run the provider through the same
//                             UsageAggregator + JSON options the default POC uses.
//   --github-copilot --raw    raw probe: hit the candidate billing endpoints and
//                             print masked bodies + the parsed product/sku/
//                             unitType/netQuantity/netAmount of each usageItem so
//                             the real (possibly post-2026-06-01) schema is visible.
//
// Privacy: the token is never printed; the resolved login is never printed (URLs
// are shown with {login} substituted); raw bodies are run through DiagnosticMasker.
internal static class GitHubCopilotPocRunner
{
    private const string ApiBaseUrl = "https://api.github.com";
    private const string ApiVersion = "2026-03-10";
    private const string UserAgent = "TokenCheckerWin-POC/0.1";
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
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/usage?year={now.Year}&month={now.Month}",
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/premium_request/usage?year={now.Year}&month={now.Month}",
            $"{ApiBaseUrl}/users/{encoded}/settings/billing/usage/summary"
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

            // In raw mode we read the body even on failure so error JSON is visible.
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
        Console.WriteLine("  body (masked; may still contain repo names — re-mask before sharing):");
        Console.WriteLine(DiagnosticMasker.Mask(probe.Body, 20000));
    }

    private static void PrintItems(IReadOnlyList<GitHubBillingUsageParser.GitHubBillingUsageItem> items)
    {
        Console.WriteLine($"  parsed usageItems: {items.Count}");
        foreach (var item in items)
        {
            Console.WriteLine(
                $"    product={item.Product ?? "(null)"}; "
                + $"sku={item.Sku ?? "(null)"}; "
                + $"unitType={item.UnitType ?? "(null)"}; "
                + $"netQuantity={Format(item.NetQuantity)}; "
                + $"netAmount={Format(item.NetAmount)}; "
                + $"copilot={GitHubBillingUsageParser.LooksLikeCopilot(item)}");
        }
    }

    private static string Format(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(null)";

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
