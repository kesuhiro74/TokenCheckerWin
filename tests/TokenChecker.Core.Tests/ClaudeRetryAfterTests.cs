using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the Retry-After interpretation that decides how long
// ClaudeUsageProvider stays off the usage endpoint after a 429.
public sealed class ClaudeRetryAfterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("soon")]
    [InlineData("not-a-date")]
    public void Parse_MissingOrUnparseableHeader_UsesTheDefaultCooldown(string? headerValue)
        => Assert.Equal(ClaudeRetryAfter.DefaultCooldown, ClaudeRetryAfter.Parse(headerValue, Now));

    [Fact]
    public void DefaultAndMaxCooldowns_AreFiveAndSixtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ClaudeRetryAfter.DefaultCooldown);
        Assert.Equal(TimeSpan.FromMinutes(60), ClaudeRetryAfter.MaxCooldown);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData(" 90 ", 90)]
    [InlineData("3600", 3600)]
    public void Parse_DeltaSeconds_IsHonored(string headerValue, int expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ClaudeRetryAfter.Parse(headerValue, Now));

    [Theory]
    [InlineData("5400")]      // 90 minutes
    [InlineData("999999")]
    public void Parse_DeltaSeconds_IsCappedAtTheMaximum(string headerValue)
        => Assert.Equal(ClaudeRetryAfter.MaxCooldown, ClaudeRetryAfter.Parse(headerValue, Now));

    [Theory]
    [InlineData("0")]
    [InlineData("-60")]
    public void Parse_ZeroOrNegativeSeconds_MeansRetryNow(string headerValue)
        => Assert.Equal(TimeSpan.Zero, ClaudeRetryAfter.Parse(headerValue, Now));

    [Fact]
    public void Parse_HttpDate_IsRelativeToNow()
    {
        // RFC 1123 form, 10 minutes after Now.
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            ClaudeRetryAfter.Parse("Fri, 16 Oct 2026 12:10:00 GMT", Now));
    }

    [Fact]
    public void Parse_HttpDate_IsCappedAtTheMaximum()
        => Assert.Equal(
            ClaudeRetryAfter.MaxCooldown,
            ClaudeRetryAfter.Parse("Fri, 16 Oct 2026 23:00:00 GMT", Now));

    [Fact]
    public void Parse_HttpDate_InThePast_MeansRetryNow()
        => Assert.Equal(
            TimeSpan.Zero,
            ClaudeRetryAfter.Parse("Fri, 16 Oct 2026 11:00:00 GMT", Now));

    [Fact]
    public void Parse_IsoDate_IsAlsoAccepted()
        => Assert.Equal(
            TimeSpan.FromMinutes(15),
            ClaudeRetryAfter.Parse("2026-10-16T12:15:00Z", Now));
}
