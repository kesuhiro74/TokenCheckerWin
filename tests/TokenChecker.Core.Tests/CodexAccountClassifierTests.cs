using TokenChecker.Core.Providers;
using Xunit;

namespace TokenChecker.Core.Tests;

// Pins the Codex account/read -> rate-limit-collection decision, now a pure
// function so the login/unsupported-type/proceed branches are verified without
// launching the real app-server.
public class CodexAccountClassifierTests
{
    [Fact]
    public void NullAccount_RequiringAuth_IsNotLoggedIn()
    {
        var decision = CodexAccountClassifier.Classify(accountIsNull: true, accountType: null, requiresOpenAiAuth: true);
        Assert.False(decision.CanProceed);
        Assert.Equal(ProviderStatus.NotLoggedIn, decision.Status);
        Assert.Equal("Codex login is required.", decision.Message);
    }

    [Fact]
    public void NullAccount_NotRequiringAuth_IsError()
    {
        var decision = CodexAccountClassifier.Classify(accountIsNull: true, accountType: null, requiresOpenAiAuth: false);
        Assert.False(decision.CanProceed);
        Assert.Equal(ProviderStatus.Error, decision.Status);
        Assert.Equal("Codex account state does not allow rate limit collection.", decision.Message);
    }

    [Fact]
    public void ChatgptAccount_CanProceed()
    {
        var decision = CodexAccountClassifier.Classify(accountIsNull: false, accountType: "chatgpt", requiresOpenAiAuth: false);
        Assert.True(decision.CanProceed);
    }

    [Fact]
    public void ApiKeyAccount_IsError_WithChatgptHint()
    {
        var decision = CodexAccountClassifier.Classify(accountIsNull: false, accountType: "apiKey", requiresOpenAiAuth: false);
        Assert.False(decision.CanProceed);
        Assert.Equal(ProviderStatus.Error, decision.Status);
        Assert.Equal("ChatGPT login is required for rate limits.", decision.Message);
    }

    [Theory]
    [InlineData("enterprise")]
    [InlineData("team")]
    [InlineData(null)] // account present but no "type" field
    public void NonChatgptNonApiKey_IsGenericError(string? accountType)
    {
        var decision = CodexAccountClassifier.Classify(accountIsNull: false, accountType: accountType, requiresOpenAiAuth: false);
        Assert.False(decision.CanProceed);
        Assert.Equal(ProviderStatus.Error, decision.Status);
        Assert.Equal("Codex account type does not support rate limit collection.", decision.Message);
    }
}
