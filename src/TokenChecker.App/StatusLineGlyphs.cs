namespace TokenChecker.App;

// Nerd Font codepoints for the minimum-mode status line. Defined ONLY as
// \u/\U escapes -- raw glyphs corrupt in some editing pipelines. Single place
// to swap a glyph later. All verified present in "Symbols Nerd Font".
internal static class StatusLineGlyphs
{
    public const string Claude   = "\U000F167A"; // nf-md-robot-happy (supplementary plane)
    public const string Codex    = "\uEA85";     // nf-cod-terminal
    public const string Clock    = "\uF017";     // nf-fa-clock-o
    public const string Reset    = "\uF0E2";     // nf-fa-undo (rotate-left arrow)
    public const string Calendar = "\uEAB0";     // nf-cod-calendar
    public const string Money    = "\uEFCA";     // nf-md-cash-multiple
}
