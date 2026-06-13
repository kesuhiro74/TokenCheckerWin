using System.Globalization;

namespace TokenChecker.App;

// Single source of truth for the daily-spend label. The currency follows the
// active UI language: Japanese shows yen (¥1,234), English shows US dollars
// ($12.34) — the same underlying spend, just the currency the reader expects.
// The yen form is a Strings entry (translatable); the dollar form is an
// invariant-culture literal so the amount always reads as "$1,234.56"
// regardless of the OS culture.
internal static class DailyCostText
{
    public static string Format(DailyCost cost)
        => Strings.IsJapanese
            ? Strings.Tf("¥{0:N0} (daily)", cost.Jpy)
            : string.Format(CultureInfo.InvariantCulture, "${0:N2} (daily)", cost.Usd);
}
