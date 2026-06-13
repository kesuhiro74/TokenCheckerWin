namespace TokenChecker.App;

// Fonts for the minimum-mode status line: a monospaced text face plus an
// optional Nerd Font icon face. Both are resolved once and kept alive for the
// app's lifetime (never disposed), matching how the other long-lived UI fonts
// are handled. The static initializer must never throw: if every candidate
// family fails we still land on Segoe UI, and a missing icon font simply
// yields Icon == null (callers skip icon runs entirely in that case).
internal static class StatusLineFonts
{
    // Text face: Cascadia Mono -> Consolas -> Segoe UI.
    public static Font Text { get; }

    // Icon face: "Symbols Nerd Font", or null when not installed (the status
    // line then renders text-only — no tofu boxes from a substitute face).
    public static Font? Icon { get; }

    public static bool IconAvailable => Icon is not null;

    static StatusLineFonts()
    {
        Text = CreateFirstInstalled(9.5F, "Cascadia Mono", "Consolas")
            ?? CreateFallbackTextFont();
        Icon = CreateFirstInstalled(10F, "Symbols Nerd Font");
    }

    // Returns a font for the first installed family in `families`, or null if
    // none is installed (or every creation attempt failed).
    private static Font? CreateFirstInstalled(float size, params string[] families)
    {
        foreach (var family in families)
        {
            try
            {
                if (UsageTheme.FontFamilyInstalled(family))
                {
                    return new Font(family, size);
                }
            }
            catch
            {
                // Try the next candidate; a font problem must never crash startup.
            }
        }

        return null;
    }

    private static Font CreateFallbackTextFont()
    {
        try
        {
            return new Font("Segoe UI", 9.5F);
        }
        catch
        {
            // Last resort so the static ctor can never throw.
            return SystemFonts.DefaultFont;
        }
    }
}
