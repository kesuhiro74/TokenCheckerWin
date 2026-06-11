namespace TokenChecker.App;

// Small shared text-geometry helper. Hosts the single implementation of the
// font-ascent calculation used by the Copilot card's baseline-shared value
// controls (MainUsageControl / TodayDeltaControl), so the two no longer carry
// identical copies.
internal static class TextMetrics
{
    // Ascent (baseline offset from the top of the text cell), in pixels.
    public static float AscentPx(Graphics g, Font font)
    {
        var family = font.FontFamily;
        var lineSpacing = family.GetLineSpacing(font.Style);
        return lineSpacing <= 0
            ? font.GetHeight(g)
            : font.GetHeight(g) * family.GetCellAscent(font.Style) / lineSpacing;
    }
}
