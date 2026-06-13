using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TokenChecker.Core.LocalCost;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the USD/JPY rate provider: response parsing, the once-per-day
// success cache, and the fixed 150 JPY fallback on HTTP/JSON failure. A fake
// HttpMessageHandler is injected so no network traffic occurs.
public class UsdJpyRateProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responder;

        public int CallCount { get; private set; }

        public FakeHandler(Func<HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder());
        }
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task GetRateAsync_ParsesRateFromResponse()
    {
        var handler = new FakeHandler(() => Json("""{"base":"USD","rates":{"JPY":156.3}}"""));
        var provider = new UsdJpyRateProvider(handler);

        var rate = await provider.GetRateAsync();

        Assert.Equal(156.3m, rate.RateJpy);
        Assert.False(rate.IsFallback);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetRateAsync_SecondCallSameDay_UsesCache()
    {
        var handler = new FakeHandler(() => Json("""{"rates":{"JPY":156.3}}"""));
        var provider = new UsdJpyRateProvider(handler);

        var first = await provider.GetRateAsync();
        var second = await provider.GetRateAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetRateAsync_HttpError_ReturnsFallback()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = new UsdJpyRateProvider(handler);

        var rate = await provider.GetRateAsync();

        Assert.Equal(UsdJpyRateProvider.DefaultRateJpy, rate.RateJpy);
        Assert.Equal(150m, rate.RateJpy);
        Assert.True(rate.IsFallback);
    }

    [Fact]
    public async Task GetRateAsync_InvalidJson_ReturnsFallback()
    {
        var handler = new FakeHandler(() => Json("not json at all"));
        var provider = new UsdJpyRateProvider(handler);

        var rate = await provider.GetRateAsync();

        Assert.True(rate.IsFallback);
        Assert.Equal(150m, rate.RateJpy);
    }

    [Fact]
    public async Task GetRateAsync_MissingJpyField_ReturnsFallback()
    {
        var handler = new FakeHandler(() => Json("""{"rates":{"EUR":0.9}}"""));
        var provider = new UsdJpyRateProvider(handler);

        var rate = await provider.GetRateAsync();

        Assert.True(rate.IsFallback);
    }

    [Fact]
    public async Task GetRateAsync_FallbackIsCachedBriefly_NoImmediateRetry()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new UsdJpyRateProvider(handler);

        var first = await provider.GetRateAsync();
        var second = await provider.GetRateAsync();

        // The fallback result is cached for 30 minutes, so the second call
        // must not hit the handler again.
        Assert.True(first.IsFallback);
        Assert.True(second.IsFallback);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void TryParseJpyRate_ValidPayload_ReturnsRate()
    {
        var root = JsonNode.Parse("""{"rates":{"JPY":156.3}}""");

        Assert.True(UsdJpyRateProvider.TryParseJpyRate(root, out var rate));
        Assert.Equal(156.3m, rate);
    }

    [Theory]
    [InlineData("""{"rates":{}}""")]
    [InlineData("""{"rates":{"JPY":"156.3"}}""")]   // string, not a number
    [InlineData("""{"rates":{"JPY":0}}""")]          // non-positive is rejected
    [InlineData("""{"rates":{"JPY":-1}}""")]
    [InlineData("""{"other":1}""")]
    [InlineData("""123""")]
    public void TryParseJpyRate_UnexpectedShape_ReturnsFalse(string json)
    {
        var root = JsonNode.Parse(json);

        Assert.False(UsdJpyRateProvider.TryParseJpyRate(root, out _));
    }

    [Fact]
    public void TryParseJpyRate_NullNode_ReturnsFalse()
        => Assert.False(UsdJpyRateProvider.TryParseJpyRate(null, out _));
}
