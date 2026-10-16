using System.Globalization;

namespace TokenChecker.App;

// Single source of truth for every spend amount shown in the UI (card money
// row, footer total, minimum-mode line). The currency follows the active UI
// language: Japanese shows yen (¥1,234), English shows US dollars ($12.34) —
// the same underlying spend, just the currency the reader expects. The yen
// forms are Strings entries (translatable); the dollar forms are
// invariant-culture literals so the amount always reads as "$1,234.56"
// regardless of the OS culture.
internal static class DailyCostText
{
    // A bare amount, e.g. "¥1,898" / "$12.34". Everything else composes this.
    public static string Amount(DailyCost cost)
        => Strings.IsJapanese
            ? Strings.Tf("¥{0:N0}", cost.Jpy)
            : string.Format(CultureInfo.InvariantCulture, "${0:N2}", cost.Usd);

    // Row labels for the card's money row (colored separately from the amount,
    // hence exposed as plain text rather than a composed string). These stay
    // English in both UI languages, by user preference. The minimum-mode line
    // tags its amounts with calendar icons instead (MinimumLineComposer).
    public static string TodayLabel => "Today";

    public static string MonthLabel => "This month";

    // The status window's footer total, flagged as an API-rate estimate.
    public static string FooterTotal(DailyCost total)
        => Strings.Tf("今月合計（API換算） {0}", Amount(total));

    // Money-row tooltip: this month's per-model breakdown, one model per line,
    // followed by the note that these are API-rate estimates. With no
    // breakdown available only the note is shown.
    public static string MonthBreakdownTooltip(IReadOnlyList<ModelCostLine>? lines)
    {
        var note = Strings.T("API換算の推定額（ローカルログから計算）");
        if (lines is null || lines.Count == 0)
        {
            return note;
        }

        var body = string.Join(
            Environment.NewLine,
            lines.Select(line => $"{line.Label}  {Amount(line.Cost)}"));
        return $"{Strings.T("今月のモデル別内訳")}{Environment.NewLine}{body}{Environment.NewLine}{Environment.NewLine}{note}";
    }
}
