using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the pure decision/parse helpers behind the "restart to apply a
// language/theme change" fix. The process plumbing (launch/await/exit) is
// integration-only and not covered here.
public class RestartCoordinatorTests
{
    [Fact]
    public void RequiresRestart_LanguageChanged_True()
    {
        var before = new AppSettings { Language = AppLanguage.Japanese };
        var after = new AppSettings { Language = AppLanguage.English };
        Assert.True(RestartCoordinator.RequiresRestart(before, after));
    }

    [Fact]
    public void RequiresRestart_ThemeChanged_True()
    {
        var before = new AppSettings { Theme = ThemeMode.Light };
        var after = new AppSettings { Theme = ThemeMode.Dark };
        Assert.True(RestartCoordinator.RequiresRestart(before, after));
    }

    [Fact]
    public void RequiresRestart_LanguageAndThemeUnchanged_False()
    {
        var before = new AppSettings { Language = AppLanguage.English, Theme = ThemeMode.Dark };
        var after = new AppSettings { Language = AppLanguage.English, Theme = ThemeMode.Dark };
        Assert.False(RestartCoordinator.RequiresRestart(before, after));
    }

    [Fact]
    public void RequiresRestart_OnlyUnrelatedFieldChanged_False()
    {
        // A refresh-interval change is applied live, so it must not trigger a restart.
        var before = new AppSettings { RefreshIntervalSeconds = 60 };
        var after = new AppSettings { RefreshIntervalSeconds = 300 };
        Assert.False(RestartCoordinator.RequiresRestart(before, after));
    }

    [Theory]
    [InlineData(new string[] { "--await-exit", "1234" }, 1234)]
    [InlineData(new string[] { "--AWAIT-EXIT", "42" }, 42)]
    [InlineData(new string[] { "--show-status", "--await-exit", "7" }, 7)]
    public void TryParseAwaitExitPid_ValidFlag_ReturnsPid(string[] args, int expected)
        => Assert.Equal(expected, RestartCoordinator.TryParseAwaitExitPid(args));

    [Fact]
    public void TryParseAwaitExitPid_AbsentOrMalformed_ReturnsNull()
    {
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new string[] { }));
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new[] { "--show-status" }));
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new[] { "--await-exit" }));        // no value
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new[] { "--await-exit", "abc" })); // non-numeric
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new[] { "--await-exit", "0" }));   // non-positive
        Assert.Null(RestartCoordinator.TryParseAwaitExitPid(new[] { "--await-exit", "-5" }));  // negative
    }
}
