using System.Net;
using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Pins the Claude usage endpoint's HTTP-status -> ProviderStatus mapping, now a
// pure function so it can be verified without a live round-trip.
public class ClaudeUsageStatusMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]            // 200
    [InlineData(HttpStatusCode.NoContent)]     // 204
    [InlineData((HttpStatusCode)299)]
    public void Success2xx_ReturnsNull(HttpStatusCode code)
        => Assert.Null(ClaudeUsageStatusMapper.Classify(code));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]  // 401
    [InlineData(HttpStatusCode.Forbidden)]     // 403
    public void AuthFailure_IsUnauthorized(HttpStatusCode code)
    {
        var failure = ClaudeUsageStatusMapper.Classify(code);
        Assert.NotNull(failure);
        Assert.Equal(ProviderStatus.Unauthorized, failure!.Value.Status);
        Assert.Equal("unauthorized", failure.Value.SafeSummary);
    }

    [Fact]
    public void TooManyRequests_IsRateLimited()
    {
        var failure = ClaudeUsageStatusMapper.Classify(HttpStatusCode.TooManyRequests);
        Assert.Equal(ProviderStatus.RateLimited, failure!.Value.Status);
        Assert.Equal("rateLimited", failure.Value.SafeSummary);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void ServerError5xx_IsError(int code)
    {
        var failure = ClaudeUsageStatusMapper.Classify((HttpStatusCode)code);
        Assert.Equal(ProviderStatus.Error, failure!.Value.Status);
        Assert.Equal("serverError", failure.Value.SafeSummary);
    }

    [Theory]
    [InlineData(100)] // 1xx informational is not a 2xx success -> httpError
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(302)] // redirect not followed -> not a success
    public void OtherNonSuccess_IsHttpError(int code)
    {
        var failure = ClaudeUsageStatusMapper.Classify((HttpStatusCode)code);
        Assert.Equal(ProviderStatus.Error, failure!.Value.Status);
        Assert.Equal("httpError", failure.Value.SafeSummary);
    }
}
