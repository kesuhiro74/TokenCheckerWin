using Xunit;

namespace TokenChecker.Core.Tests;

// Locks down the privacy invariant: the diagnostic masker must scrub emails,
// absolute paths, secret assignments and long token-like blobs, and truncate.
public class DiagnosticMaskerTests
{
    [Fact]
    public void Null_ReturnsEmpty() => Assert.Equal("", DiagnosticMasker.Mask(null, 100));

    [Fact]
    public void Whitespace_ReturnsEmpty() => Assert.Equal("", DiagnosticMasker.Mask("   ", 100));

    [Fact]
    public void Email_IsMasked()
        => Assert.Equal("contact <email>", DiagnosticMasker.Mask("contact user@example.com", 200));

    [Fact]
    public void WindowsPath_IsMasked()
        => Assert.Equal("at <path> now", DiagnosticMasker.Mask(@"at C:\Users\admin\.claude now", 200));

    [Fact]
    public void PosixPath_IsMasked()
        => Assert.Equal("home <path> end", DiagnosticMasker.Mask("home /home/user/.config end", 200));

    [Theory]
    [InlineData("token=sk_live_abc123", "token=<redacted>")]
    [InlineData("secret = hunter2", "secret=<redacted>")]
    [InlineData("key: ABC", "key=<redacted>")]
    [InlineData("authorization=xyz", "authorization=<redacted>")]
    public void SecretAssignment_IsRedacted(string input, string expected)
        => Assert.Equal(expected, DiagnosticMasker.Mask(input, 200));

    [Fact]
    public void LongAlphanumericBlob_IsRedacted()
        => Assert.Equal("id <redacted> end",
            DiagnosticMasker.Mask("id 0123456789abcdef0123456789abcdef012345 end", 200));

    [Fact]
    public void Truncates_To_MaxLength()
        => Assert.Equal("abcd", DiagnosticMasker.Mask("abcdefghij", 4));

    [Fact]
    public void Combined_AllRulesApply()
        => Assert.Equal("e <email> p <path> t token=<redacted>",
            DiagnosticMasker.Mask(@"e a@b.io p C:\x\y t token=ZZZ", 200));
}
