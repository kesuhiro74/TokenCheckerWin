using Xunit;

namespace TokenChecker.Core.Tests;

// Edge cases beyond the core DiagnosticMaskerTests: UNC paths, non-ASCII paths,
// JWTs, and the length-budget guard. These harden the privacy net for shapes the
// providers do not emit today but could after a refactor — and guard against the
// new rules over-masking ordinary diagnostic text.
public class DiagnosticMaskerEdgeTests
{
    [Fact]
    public void UncPath_IsMasked()
        => Assert.Equal("at <path> now", DiagnosticMasker.Mask(@"at \\server\share\file now", 200));

    [Fact]
    public void JapanesePath_IsMasked()
        => Assert.Equal("at <path> now", DiagnosticMasker.Mask(@"at C:\ユーザー\admin\.claude now", 200));

    [Fact]
    public void Jwt_IsRedactedWhole()
        => Assert.Equal("jwt <redacted> end", DiagnosticMasker.Mask(
            "jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c end",
            200));

    [Fact]
    public void ZeroMaxLength_ReturnsEmpty()
        => Assert.Equal("", DiagnosticMasker.Mask("hello", 0));

    [Fact]
    public void NegativeMaxLength_ReturnsEmpty_DoesNotThrow()
        => Assert.Equal("", DiagnosticMasker.Mask("hello", -5));

    [Fact]
    public void OrdinaryDiagnostic_PassesThroughUnchanged()
    {
        // The real Codex debug summary — none of the rules (incl. the new UNC/JWT
        // ones) must touch it.
        const string diagnostic = "accountNull=false; accountType=chatgpt; requiresOpenaiAuth=false; planTypePresent=true;";
        Assert.Equal(diagnostic, DiagnosticMasker.Mask(diagnostic, 200));
    }
}
