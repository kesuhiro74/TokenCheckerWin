using System.Text.Json.Nodes;
using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Pins the Codex JSON-RPC error -> "code=...; message=..." summary, now a pure
// function. Guards the privacy invariant: a numeric code passes through, but any
// other code kind and the message both go through DiagnosticMasker / redaction so
// nothing sensitive can leak into a diagnostic string.
public class CodexErrorSummarizerTests
{
    [Fact]
    public void NumericCode_IsEmittedVerbatim()
    {
        var error = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
        Assert.Equal("code=-32601; message=Method not found", CodexErrorSummarizer.Summarize(error));
    }

    [Fact]
    public void StringCode_IsRedacted_NotLeaked()
    {
        // A string code could carry arbitrary text; it must never be printed raw.
        var error = new JsonObject { ["code"] = "secret-internal-code", ["message"] = "boom" };
        var summary = CodexErrorSummarizer.Summarize(error);
        Assert.Equal("code=<redacted>; message=boom", summary);
        Assert.DoesNotContain("secret-internal-code", summary);
    }

    [Fact]
    public void MissingCode_IsUnknown()
    {
        var error = new JsonObject { ["message"] = "no code here" };
        Assert.Equal("code=unknown; message=no code here", CodexErrorSummarizer.Summarize(error));
    }

    [Fact]
    public void MessageWithEmailAndToken_IsMasked()
    {
        var error = new JsonObject
        {
            ["code"] = 500,
            ["message"] = "failed for user jane.doe@example.com with token=abcDEF1234567890abcDEF1234567890ZZ"
        };
        var summary = CodexErrorSummarizer.Summarize(error);

        Assert.StartsWith("code=500; message=", summary);
        Assert.DoesNotContain("jane.doe@example.com", summary);
        Assert.DoesNotContain("abcDEF1234567890abcDEF1234567890ZZ", summary);
        Assert.Contains("<email>", summary);
        Assert.Contains("<redacted>", summary);
    }

    [Fact]
    public void NullError_UsesDefaults()
    {
        var summary = CodexErrorSummarizer.Summarize(null);
        Assert.Equal("code=unknown; message=Codex app-server returned an error.", summary);
    }
}
