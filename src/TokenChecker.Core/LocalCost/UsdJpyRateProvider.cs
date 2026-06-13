using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.LocalCost;

// USD→JPY conversion rate with a flag telling whether the fixed fallback rate
// was used (so the UI can mark the converted amount as approximate).
public sealed record UsdJpyRate(decimal RateJpy, bool IsFallback);

// Fetches the USD/JPY rate from the free Frankfurter API at most once per
// local day, falling back to a fixed 150 JPY/USD when the request fails.
//
// Caching is in-memory only — the rate is never persisted to disk (the app
// only writes settings.json / last_usage.json / copilot_usage.json). A
// successful rate is reused for the rest of the local day; a fallback result
// is cached for 30 minutes so a transient outage is retried without hammering
// the API. No exception escapes GetRateAsync on failure, and the response
// body is never retained.
public sealed class UsdJpyRateProvider
{
    public const decimal DefaultRateJpy = 150m;

    private const string Endpoint = "https://api.frankfurter.dev/v1/latest?base=USD&symbols=JPY";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FallbackCacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UsdJpyRate? _cachedRate;
    private DateTime _cachedOnLocalDate;
    private DateTimeOffset _fallbackCachedAtUtc;

    public UsdJpyRateProvider(HttpMessageHandler? handler = null)
    {
        // Per-request timeouts are driven by a linked CancellationTokenSource
        // (same pattern as ClaudeUsageProvider), so the client itself never
        // times out.
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TokenCheckerWin");
    }

    public async Task<UsdJpyRate> GetRateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (GetCachedRate() is { } cached)
            {
                return cached;
            }

            var fetched = await TryFetchRateAsync(cancellationToken).ConfigureAwait(false);
            if (fetched is { } rate)
            {
                var result = new UsdJpyRate(rate, IsFallback: false);
                _cachedRate = result;
                _cachedOnLocalDate = DateTime.Today;
                return result;
            }

            var fallback = new UsdJpyRate(DefaultRateJpy, IsFallback: true);
            _cachedRate = fallback;
            _fallbackCachedAtUtc = DateTimeOffset.UtcNow;
            return fallback;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Pure JSON extraction for {"rates":{"JPY":156.3}}. Public because Core
    // has no InternalsVisibleTo, so testable helpers are public (same
    // convention as GitHubBillingUsageParser).
    public static bool TryParseJpyRate(JsonNode? root, out decimal rateJpy)
    {
        rateJpy = 0m;

        try
        {
            var node = root?["rates"]?["JPY"];
            if (node is null || node.GetValueKind() != JsonValueKind.Number)
            {
                return false;
            }

            var parsed = node.GetValue<decimal>();
            if (parsed <= 0m)
            {
                return false;
            }

            rateJpy = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private UsdJpyRate? GetCachedRate()
    {
        if (_cachedRate is null)
        {
            return null;
        }

        if (!_cachedRate.IsFallback)
        {
            // A real rate is good for the rest of the local day.
            return _cachedOnLocalDate == DateTime.Today ? _cachedRate : null;
        }

        // A fallback result is only held briefly so the next caller retries.
        return DateTimeOffset.UtcNow - _fallbackCachedAtUtc < FallbackCacheDuration
            ? _cachedRate
            : null;
    }

    private async Task<decimal?> TryFetchRateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var response = await _httpClient
                .GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // The body is parsed and immediately discarded; it is never logged
            // or stored.
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return TryParseJpyRate(JsonNode.Parse(body), out var rate) ? rate : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-requested cancellation is not a fetch failure.
            throw;
        }
        catch
        {
            // Timeout, network error, malformed JSON, ...: use the fallback.
            return null;
        }
    }
}
