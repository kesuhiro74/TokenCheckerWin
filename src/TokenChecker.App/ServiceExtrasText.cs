using System.Globalization;
using TokenChecker.Core;

namespace TokenChecker.App;

// Pure text composition for the display-only extras a provider reports
// (ServiceExtras): the per-model weekly limits row and the "extra info" row
// (real money / credit spend, Codex reset credits, credit balance).
//
// Everything here is a number, a date or a plan/model display name — see the
// privacy note on ServiceExtras — so these strings are safe for the UI.
// Numbers are formatted with the invariant culture so the output does not drift
// with the OS culture; dates are converted to LOCAL time (the user reads local
// clocks everywhere else in this app).
internal static class ServiceExtrasText
{
    // Joins the items of a row. Deliberately not translated: it is punctuation
    // shared by both languages (same role as the "|" in the minimum-mode line).
    public const string ItemSeparator = " ・ ";

    // "Fable 週 4%" per scoped limit, joined. Entries without a percentage are
    // skipped: the label alone tells the user nothing.
    public static string ScopedLimits(ServiceExtras? extras)
    {
        if (extras?.ScopedLimits is not { Count: > 0 } limits)
        {
            return string.Empty;
        }

        var items = limits
            .Where(limit => limit.UsedPercent is not null && !string.IsNullOrWhiteSpace(limit.Label))
            .Select(limit => Strings.Tf(
                "{0} 週 {1}%",
                limit.Label.Trim(),
                Math.Round(limit.UsedPercent!.Value).ToString("0", CultureInfo.InvariantCulture)));

        return string.Join(ItemSeparator, items);
    }

    // Highest scoped-limit percentage, for the row's severity color (the 80/95
    // escalation itself stays in UsageTheme). Null when there is nothing to color.
    public static double? MaxScopedPercent(ServiceExtras? extras)
    {
        if (extras?.ScopedLimits is not { Count: > 0 } limits)
        {
            return null;
        }

        double? max = null;
        foreach (var limit in limits)
        {
            if (limit.UsedPercent is { } percent && (max is null || percent > max))
            {
                max = percent;
            }
        }

        return max;
    }

    // The "extra info" row: overage/credit spend, then Codex's reset credits,
    // then the credit balance. Empty when the provider reported none of them.
    public static string Info(ServiceExtras? extras)
    {
        if (extras is null)
        {
            return string.Empty;
        }

        var items = new List<string>(3);

        if (extras.ExtraSpend is { } spend)
        {
            items.Add(SpendText(spend));
        }

        if (extras.ResetCreditsAvailable is { } resetCredits && resetCredits > 0)
        {
            items.Add(ResetCreditsText(resetCredits, extras.ResetCreditsNextExpiry));
        }

        if (extras.CreditsUnlimited)
        {
            items.Add(Strings.T("クレジット 無制限"));
        }
        else if (extras.CreditsBalance is { } balance)
        {
            items.Add(Strings.Tf("クレジット {0}", CostBreakdown.FormatNumber(balance)));
        }

        return string.Join(ItemSeparator, items);
    }

    // "追加利用 $12.34 / $50.00" for money, "月次上限 30 / 100 クレジット" for
    // Codex's credit-denominated spend limit.
    internal static string SpendText(ExtraSpend spend)
    {
        if (IsCredits(spend.Unit))
        {
            return spend.Limit is { } creditLimit
                ? Strings.Tf(
                    "月次上限 {0} / {1} クレジット",
                    CostBreakdown.FormatNumber(spend.Used),
                    CostBreakdown.FormatNumber(creditLimit))
                : Strings.Tf("追加利用 {0} クレジット", CostBreakdown.FormatNumber(spend.Used));
        }

        return spend.Limit is { } limit
            ? Strings.Tf("追加利用 {0} / {1}", Money(spend.Used, spend.Unit), Money(limit, spend.Unit))
            : Strings.Tf("追加利用 {0}", Money(spend.Used, spend.Unit));
    }

    // "リセット券 2枚（10/30 まで）"; the expiry is the local month/day.
    internal static string ResetCreditsText(int count, DateTimeOffset? nextExpiryUtc)
        => nextExpiryUtc is { } expiry
            ? Strings.Tf(
                "リセット券 {0}枚（{1} まで）",
                count.ToString("0", CultureInfo.InvariantCulture),
                expiry.ToLocalTime().ToString("M/d", CultureInfo.InvariantCulture))
            : Strings.Tf("リセット券 {0}枚", count.ToString("0", CultureInfo.InvariantCulture));

    private static bool IsCredits(string? unit)
        => string.Equals(unit, "credits", StringComparison.OrdinalIgnoreCase);

    // Currency amount for a 3-letter code: a symbol for the few currencies
    // these APIs actually report, otherwise "12.34 XYZ" so an unknown code is
    // still readable (and never mislabeled with the wrong symbol).
    private static string Money(decimal value, string? unit)
    {
        var code = unit?.Trim().ToUpperInvariant() ?? string.Empty;
        return code switch
        {
            "USD" => string.Format(CultureInfo.InvariantCulture, "${0:N2}", value),
            "EUR" => string.Format(CultureInfo.InvariantCulture, "€{0:N2}", value),
            "GBP" => string.Format(CultureInfo.InvariantCulture, "£{0:N2}", value),
            "JPY" => string.Format(CultureInfo.InvariantCulture, "¥{0:N0}", value),
            "" => CostBreakdown.FormatNumber(value),
            _ => $"{CostBreakdown.FormatNumber(value)} {code}"
        };
    }
}
