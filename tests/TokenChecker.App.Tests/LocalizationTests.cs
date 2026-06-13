using System.Globalization;
using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the UI localization infrastructure: language resolution, the
// English string table (parity / fallback), and the Language setting.
[Collection("StringsLanguage")]
public class LocalizationTests
{
    [Theory]
    [InlineData((int)AppLanguage.Japanese, "en-US", true)]   // explicit wins over culture
    [InlineData((int)AppLanguage.English, "ja-JP", false)]
    public void Resolver_ExplicitLanguage_IgnoresCulture(int lang, string culture, bool expected)
        => Assert.Equal(expected, LanguageResolver.ResolveJapanese((AppLanguage)lang, new CultureInfo(culture)));

    [Theory]
    [InlineData("ja-JP", true)]
    [InlineData("ja", true)]
    [InlineData("en-US", false)]
    [InlineData("fr-FR", false)]
    public void Resolver_System_FollowsCulture(string culture, bool expected)
        => Assert.Equal(expected, LanguageResolver.ResolveJapanese(AppLanguage.System, new CultureInfo(culture)));

    [Fact]
    public void Strings_EnglishMode_TranslatesKnownKeys()
    {
        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("Theme", Strings.T("テーマ"));
            Assert.Equal("in 2m", Strings.Tf("あと{0}分", 2));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Strings_JapaneseMode_ReturnsSourceKey()
    {
        Strings.Apply(japanese: true);
        Assert.Equal("テーマ", Strings.T("テーマ"));
        Assert.Equal("あと2分", Strings.Tf("あと{0}分", 2));
    }

    [Fact]
    public void Strings_MissingKey_FallsBackToInput()
    {
        Strings.Apply(japanese: false);
        try
        {
            Assert.Equal("未登録テキスト", Strings.T("未登録テキスト"));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }

    [Fact]
    public void Strings_EveryEnglishEntry_IsNonEmpty()
    {
        Assert.NotEmpty(Strings.English);
        Assert.All(Strings.English, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.False(string.IsNullOrWhiteSpace(kv.Value));
        });
    }

    [Fact]
    public void AppSettings_Normalize_UndefinedLanguage_BecomesSystem()
    {
        var settings = new AppSettings { Language = (AppLanguage)99 };
        settings.Normalize();
        Assert.Equal(AppLanguage.System, settings.Language);
    }

    [Fact]
    public void AppSettings_Clone_CopiesLanguage()
        => Assert.Equal(AppLanguage.English, new AppSettings { Language = AppLanguage.English }.Clone().Language);
}
