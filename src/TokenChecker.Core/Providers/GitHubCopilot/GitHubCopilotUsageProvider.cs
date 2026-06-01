using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers.GitHubCopilot;

// EXPERIMENTAL (POC). Reads the authenticated user's monthly GitHub billing
// usage and surfaces the Copilot consumption for the current month.
//
// IMPORTANT scope/limitations (see docs/experiments/github-copilot/README.md):
//  - Covers ONLY usage billed directly to the personal account. Org/Enterprise
//    managed Copilot usage does NOT appear on this personal billing endpoint.
//  - GitHub exposes no real-time rate-limit window (no utilization% / resets_at)
//    for Copilot like Claude/Codex; only monthly consumption. So Used is set,
//    but Limit/Remaining/UsedPercent stay null (the monthly allowance is not in
//    the API) and ResetAtUtc is a COMPUTED month boundary, not from the API.
//  - 403/404/503 can mean missing permission OR an account not on the enhanced
//    billing platform OR an unsupported/transient state — the POC just reports
//    the mapped status; use --raw to disambiguate.
//
// Mirrors ClaudeUsageProvider's conventions: static HttpClient with an infinite
// client timeout + a per-call linked CTS, never throws on the happy path, and
// all diagnostic text goes through DiagnosticMasker.
public sealed class GitHubCopilotUsageProvider : IUsageProvider
{
    private const string ApiBaseUrl = "https://api.github.com";
    private const string UserEndpoint = ApiBaseUrl + "/user";
    private const string ApiVersion = "2026-03-10";
    private const string UserAgent = "TokenCheckerWin-POC/0.1";
    private const string AcceptHeader = "application/vnd.github+json";
    private const long MonthWindowMins = 43200; // ~30 days, nominal monthly bucket.

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

            // 2) Read this month's billing usage for that user.
            var now = DateTimeOffset.UtcNow;
            var usageUrl = $"{ApiBaseUrl}/users/{Uri.EscapeDataString(login)}/settings/billing/usage"
                + $"?year={now.Year}&month={now.Month}";

            var billingProbe = await ProbeAsync(usageUrl, token, timeout.Token).ConfigureAwait(false);
            if (!billingProbe.Success)
            {
                var (status, summary) = MapFailure(billingProbe, isBilling: true);
                return Failure(
                    status,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, billing: summary));
            }

            // A 200 with a body we can't make sense of must NOT be reported as a
            // confident "Available, used=0". Distinguish parse failure from a
            // missing/!array usageItems shape; only a genuine empty array means
            // zero consumption.
            var root = SafeParse(billingProbe.Body);
            if (root is null)
            {
                return Failure(
                    ProviderStatus.Error,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, billing: "invalidJson"));
            }

            if (!GitHubBillingUsageParser.TryParseUsageItems(root, out var items))
            {
                return Failure(
                    ProviderStatus.Error,
                    BuildMessage(tokenPresent: true, userApi: "ok", loginResolved: true, billing: "unexpectedShape"));
            }

            // An empty usageItems array is legitimate (no consumption this month).
            var copilotItems = items.Where(GitHubBillingUsageParser.IsCopilotPremiumRequestUsage).ToArray();

            // Consumption = sum of quantity (fallback grossQuantity). netQuantity
            // is deliberately NOT used: it was observed as 0/null and represents a
            // post-discount value, not the request count. The shared RateLimitWindow
            // model carries Used as long?, so the window keeps the rounded value and
            // the precise decimal is surfaced in the (masked) diagnostic Message.
            var usedExact = copilotItems.Sum(i => i.Quantity ?? i.GrossQuantity ?? 0d);
            var used = (long)Math.Round(usedExact, MidpointRounding.AwayFromZero);

            var resetAtUtc = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var window = new RateLimitWindow(
                Name: $"Copilot (month {now:yyyy-MM})",
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
    // carries a "rate limit" message (e.g. "You have exceeded a secondary rate
    // limit. Please wait ..."). We classify on that text only; the body itself is
    // never logged or surfaced.
    private static bool BodyLooksRateLimited(string? body)
        => body is not null
            && body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    // A 403 is mapped to Unauthorized (most often missing scope / "Plan: read"
    // for fine-grained PATs), unless it carries rate-limit signals (Retry-After,
    // x-ratelimit-remaining:0, or a "rate limit" message body) in which case it is
    // RateLimited. A 404 on the billing call is reported distinctly (notFound(404))
    // because it usually
    // means the account isn't on the enhanced billing platform rather than a
    // login problem — there is no CLI here, so NotInstalled is never returned.
    private static (ProviderStatus Status, string Summary) MapFailure(HttpProbe probe, bool isBilling)
    {
        if (probe.RateLimited)
        {
            return (ProviderStatus.RateLimited, "rateLimited");
        }

        return probe.Code switch
        {
            401 => (ProviderStatus.Unauthorized, "unauthorized"),
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
        string? billing = null,
        int? itemsTotal = null,
        int? itemsCopilot = null,
        double? usedExact = null)
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
            // Precise (decimal) request count. The window's long Used carries the
            // rounded value and ResetAtUtc the reset, so neither is repeated here —
            // that also keeps this string inside the 160-char mask budget.
            builder.Append($"usedExact={usedExact.Value.ToString(CultureInfo.InvariantCulture)}; unit=requests; ");
        }

        return DiagnosticMasker.Mask(builder.ToString(), 160);
    }

    private static string FormatBool(bool value)
        => value ? "true" : "false";

    private sealed record HttpProbe(int Code, bool Success, bool RateLimited, string? Body);
}
