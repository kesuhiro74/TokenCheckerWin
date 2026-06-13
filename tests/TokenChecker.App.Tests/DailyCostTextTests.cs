using System.Globalization;
using Xunit;

namespace TokenChecker.App.Tests;

// The daily-spend label follows the UI language: yen in Japanese, US dollars in
// English. Shares the StringsLanguage collection so flipping the global language
// flag does not race other language-sensitive tests.
[Collection("StringsLanguage")]
public class DailyCostTextTests
{
    [Fact]
    public void Format_Japanese_ShowsYenNoDecimals()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        Strings.Apply(japanese: true);
        try
        {
            Assert.Equal("¥1,235 (daily)", DailyCostText.Format(new DailyCost(8.5m, 1234.5m)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Format_English_ShowsDollarsWithCents()
    {
        Strings.Apply(japanese: false);
        try
        {
            // USD value drives the dollar form; the JPY value is ignored.
            Assert.Equal("$12.34 (daily)", DailyCostText.Format(new DailyCost(12.34m, 1900m)));
            Assert.Equal("$1,234.50 (daily)", DailyCostText.Format(new DailyCost(1234.5m, 190000m)));
        }
        finally
        {
            Strings.Apply(japanese: true);
        }
    }
}
