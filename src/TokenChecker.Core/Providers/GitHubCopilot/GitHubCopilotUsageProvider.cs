using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// Reads the authenticated user's monthly GitHub Copilot AI Credits consumption
// and surfaces it as a single monthly window. GitHub Copilot moved to usage-based
// billing (AI Credits) on 2026-06-01.
//
// IMPORTANT scope/limitations (see docs/experiments/github-copilot/README.md):
//  - Covers ONLY usage billed directly to the personal account. Org/Enterprise
//    managed Copilot usage does NOT appear on this personal billing endpoint.
//  - There is no real-time rate-limit window (no utilization% / resets_at) for
//    Copilot like Claude/Codex; only monthly consumption. So Used is set, but
//    Limit/Remaining/UsedPercent stay null (the monthly allowance is NOT in the
//    API — the App overlays a plan-based allowance at display time) and
//    ResetAtUtc is a COMPUTED calendar-month boundary, not from the API.
//  - 403/404/503 can mean missing permission OR an account not on the enhanced
//    billing platform OR an unsupported/transient state.
//
// Mirrors ClaudeUsageProvider's conventions: static HttpClient with an infinite
// client timeout + a per-call linked CTS, never throws on the happy path, and
// all diagnostic text goes through DiagnosticMasker. The token is read from the
// GITHUB_TOKEN environment variable only — never stored, never emitted.
public sealed class GitHubCopilotUsageProvider : IUsageProvider
{
    private const string ApiBaseUrl = "https://api.github.com";
    private const string UserEndpoint = ApiBaseUrl + "/user";
    private const string ApiVersion = "2026-03-10";
    private const string UserAgent = "TokenCheckerWin/0.4.1";
    private const string AcceptHeader = "application/vnd.github+json";
    private const long MonthWindowMins = 43200; // ~30 days, nominal monthly bucket.
    private const string WindowName = "GitHub Copilot AI Credits";

    private static readonly TimeSpan UsageTimeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public string ServiceName => "GitHub Copilot";

    public async Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failure(ProviderStatus.NotLoggedIn, BuildMessage(tokenPresent: false));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UsageTimeout);

            // 1) Resolve the authenticated login (billing path needs {username}).
            var userProbe = await ProbeAsync(UserEndpoint, token, timeout.Token).ConfigureAwait(false);
            if (!userProbe.Success)
            {
                var (status, summary) = MapFailure(userProbe, isBilling: false);
                return Failure(status, BuildMessage(tokenPresent: true, userApi: summary));
            }

            if (!GitHubBillingUsageParser.TryGetLogin(SafeParse(userProbe.Body), out var login))
            {
                return Failure(
                    ProviderStatus.Error,
                    BuildMessage(tokenPresent: true, userApi: "unexpectedJson", loginResolved: false));
            }

            // 2) Read this month's AI Credits usage. The dedicated ai_credit/usage
            //    endpoint is tried first; ONLY a 404 (endpoint absent/unsupported)
            //    falls back to the generic billing usage endpoint. Auth/rate/server
            //    failures (401/403/429/5xx) are endpoint-independent, so they are
            //    mapped immediately without a fallback.
            var now = DateTimeOffset.UtcNow;
            var encoded = Uri.EscapeDataString(login);
            var aiCreditUrl = $"{ApiBaseUrl}/users/{encoded}/settings/billing/ai_credit/usage"
                + $"?year={now.Year}&month={now.Month}";

            var endpoint = "ai_credit";
            var billingProbe = await ProbeAsync(aiCreditUrl, token, timeout.Token).ConfigureAwait(false);
            if (!billingProbe.Success && billingProbe.Code == 404)
            {
                var usageUrl = $"{ApiBaseUrl}/users/{encoded}/settings/billing/usage"
                    + $"?year={now.Year}&month={now.Month}";
                endpoint = "usage";
                billingProbe = await ProbeAsync(usageUrl, token, timeout.Token).ConfigureAwait(false);
            }

            if (!billingProbe.Success)
            {
                var (status, summary) = MapFailure(billingProbe, isBilling: true);
                return Failure(
                    status,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, endpoint: endpoint, billing: summary));
            }

            // A 200 with a body we can't make sense of must NOT be reported as a
            // confident "Available, used=0". Distinguish parse failure from a
            // missing/!array usageItems shape; only a genuine empty array (or an
            // array with no Copilot AI-credit rows) means zero consumption.
            var root = SafeParse(billingProbe.Body);
            if (root is null)
            {
                return Failure(
                    ProviderStatus.Error,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, endpoint: endpoint, billing: "invalidJson"));
            }

            if (!GitHubBillingUsageParser.TryParseUsageItems(root, out var items))
            {
                return Failure(
                    ProviderStatus.Error,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, endpoint: endpoint, billing: "unexpectedShape"));
            }

            var copilotItems = items.Where(GitHubBillingUsageParser.IsCopilotAiCreditUsage).ToArray();

            // Sum credits in decimal, then round the total ONCE (away-from-zero).
            // The shared RateLimitWindow carries Used as long?, so the window keeps
            // the rounded value and the precise decimal is surfaced only in the
            // (masked) diagnostic Message.
            var usedExact = copilotItems.Sum(GitHubBillingUsageParser.CreditsForRow);
            var used = (long)Math.Round(usedExact, MidpointRounding.AwayFromZero);

            var resetAtUtc = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var window = new RateLimitWindow(
                Name: WindowName,
                ResetAtUtc: resetAtUtc,
                Used: used,
                Limit: null,
                Remaining: null,
                UsedPercent: null,
                WindowDurationMins: MonthWindowMins);

            var message = BuildMessage(
                tokenPresent: true,
                userApi: "ok",
                loginResolved: true,
                endpoint: endpoint,
                billing: "available",
                itemsTotal: items.Count,
                itemsCopilot: copilotItems.Length,
                usedExact: usedExact);

            return new ServiceUsage(ServiceName, ProviderStatus.Available, message, new[] { window });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(ProviderStatus.Error, BuildMessage(tokenPresent: true, billing: "timeout"));
        }
        catch (JsonException)
        {
            return Failure(ProviderStatus.Error, BuildMessage(tokenPresent: true, billing: "invalidJson"));
        }
        catch
        {
            return Failure(ProviderStatus.Error, BuildMessage(tokenPresent: true, billing: "usageReadFailed"));
        }
    }

    private static async Task<HttpProbe> ProbeAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd(AcceptHeader);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);

        using var response = await HttpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var code = (int)response.StatusCode;

        // Read the body on success (we need usageItems) and on 403 (so a
        // primary/secondary rate limit can be told apart from a genuine auth
        // failure). The 403 body is used ONLY for classification here — it is
        // never placed in the diagnostic Message. Other error bodies are skipped.
        string? body = (response.IsSuccessStatusCode || code == 403)
            ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var rateLimited = code == 429
            || (code == 403 && (IsRateLimitSignal(response) || BodyLooksRateLimited(body)));

        return new HttpProbe(code, response.IsSuccessStatusCode, rateLimited, body);
    }

    private static bool IsRateLimitSignal(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not null)
        {
            return true;
        }

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var values))
        {
            foreach (var value in values)
            {
                if (string.Equals(value.Trim(), "0", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // GitHub reports primary/secondary rate limits with a 403/429 whose JSON body
    // carries a "rate limit" message. We classify on that text only; the body
    // itself is never logged or surfaced.
    private static bool BodyLooksRateLimited(string? body)
        => body is not null
            && body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    // A 403 maps to Unauthorized (most often a fine-grained PAT missing "Plan:
    // Read", or an account outside the enhanced billing platform / not personally
    // billed), unless it carries rate-limit signals, in which case it is
    // RateLimited. 401 -> Unauthorized(401), 403 -> Unauthorized(403); the App
    // tells 401 vs 403 apart by whether the masked Message contains "(403)". A
    // 404 on billing is reported distinctly (notFound(404)). There is no CLI here,
    // so NotInstalled is never returned.
    private static (ProviderStatus Status, string Summary) MapFailure(HttpProbe probe, bool isBilling)
    {
        if (probe.RateLimited)
        {
            return (ProviderStatus.RateLimited, "rateLimited");
        }

        return probe.Code switch
        {
            401 => (ProviderStatus.Unauthorized, "unauthorized(401)"),
            403 => (ProviderStatus.Unauthorized, "unauthorized(403)"),
            404 => (ProviderStatus.Error, isBilling ? "notFound(404)" : "notFound"),
            >= 500 => (ProviderStatus.Error, $"serverError({probe.Code})"),
            _ => (ProviderStatus.Error, $"httpError({probe.Code})")
        };
    }

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

    private ServiceUsage Failure(ProviderStatus status, string message)
        => new(ServiceName, status, message, Array.Empty<RateLimitWindow>());

    // Builds a short, masked key=value diagnostic. Only booleans/counts/status
    // tokens are included — never the token, its length, the login, repository
    // names, or any URL. DiagnosticMasker runs as a final safety net.
    private static string BuildMessage(
        bool tokenPresent,
        string? userApi = null,
        bool? loginResolved = null,
        string? endpoint = null,
        string? billing = null,
        int? itemsTotal = null,
        int? itemsCopilot = null,
        decimal? usedExact = null)
    {
        var builder = new StringBuilder();
        builder.Append($"tokenPresent={FormatBool(tokenPresent)}; ");
        builder.Append("tokenSource=env; ");

        if (userApi is not null)
        {
            builder.Append($"userApi={userApi}; ");
        }

        if (loginResolved is not null)
        {
            builder.Append($"loginResolved={FormatBool(loginResolved.Value)}; ");
        }

        if (endpoint is not null)
        {
            builder.Append($"endpoint={endpoint}; ");
        }

        if (billing is not null)
        {
            builder.Append($"billing={billing}; ");
        }

        if (itemsTotal is not null)
        {
            builder.Append($"itemsTotal={itemsTotal}; ");
        }

        if (itemsCopilot is not null)
        {
            builder.Append($"itemsCopilot={itemsCopilot}; ");
        }

        if (usedExact is not null)
        {
            // Precise (decimal) credit total. The window's long Used carries the
            // rounded value and ResetAtUtc the reset, so neither is repeated here —
            // that also keeps this string inside the 160-char mask budget.
            builder.Append($"usedExact={usedExact.Value.ToString(CultureInfo.InvariantCulture)}; unit=credits; ");
        }

        return DiagnosticMasker.Mask(builder.ToString(), 160);
    }

    private static string FormatBool(bool value)
        => value ? "true" : "false";

    private sealed record HttpProbe(int Code, bool Success, bool RateLimited, string? Body);
}
