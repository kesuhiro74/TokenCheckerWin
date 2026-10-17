namespace TokenChecker.App;

// The Copilot card's "this month by model" block: a small muted caption plus up
// to CopilotModelBreakdown.MaxModelLines + 1 rows, each "model name" on the left
// (ellipsized) and "1,234（26%）" right-aligned.
//
// It is ONE owner-drawn control rather than a dozen Labels: the card paints a
// glass background, and every extra transparent child is another control to
// keep in sync (and another node CopilotWindow.AttachHandlers has to wire for
// drag/hover). Transparency follows MainUsageControl's pattern.
internal sealed class CopilotBreakdownControl : Control
{
    // One text row. The caption occupies the first row, the breakdown lines the
    // rest, so the control's height is always (1 + lines) * RowHeight.
    public const int RowHeight = 16;
    private const float FontSize = 8.5F;
    // Minimum gap between an ellipsized label and the right-aligned value.
    private const int ValueGap = 8;

    private string _caption = string.Empty;
    private IReadOnlyList<CopilotModelLine> _lines = Array.Empty<CopilotModelLine>();

    public CopilotBreakdownControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    // The pixel height this block needs for the given number of rows (caption
    // included). Static so the card can do its layout math without reading back
    // any control state.
    public static int HeightFor(int lineCount)
        => lineCount <= 0 ? 0 : (lineCount + 1) * RowHeight;

    public void SetContent(string caption, IReadOnlyList<CopilotModelLine> lines)
    {
        _caption = caption ?? string.Empty;
        _lines = lines ?? Array.Empty<CopilotModelLine>();
        Invalidate();
    }

    // Transparent: let the glass card paint its gradient behind us.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (BackColor == Color.Transparent && Parent is not null)
        {
            var g = e.Graphics;
            var state = g.Save();
            g.TranslateTransform(-Left, -Top);
            using var pe = new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height));
            InvokePaintBackground(Parent, pe);
            InvokePaint(Parent, pe);
            g.Restore(state);
            return;
        }

        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_lines.Count == 0)
        {
            return;
        }

        var g = e.Graphics;
        using var font = UsageTheme.CreateCopilotFont(FontSize);

        const TextFormatFlags labelFlags = TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis;
        const TextFormatFlags valueFlags = TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.Right;

        TextRenderer.DrawText(
            g, _caption, font, new Rectangle(0, 0, Width, RowHeight), UsageTheme.MutedText, labelFlags);

        for (var i = 0; i < _lines.Count; i++)
        {
            var top = (i + 1) * RowHeight;
            var value = CopilotModelBreakdown.ValueText(_lines[i]);
            var valueWidth = TextRenderer
                .MeasureText(g, value, font, new Size(int.MaxValue, int.MaxValue), valueFlags)
                .Width;
            valueWidth = Math.Min(valueWidth, Width);

            var labelWidth = Math.Max(0, Width - valueWidth - ValueGap);
            TextRenderer.DrawText(
                g,
                _lines[i].Label,
                font,
                new Rectangle(0, top, labelWidth, RowHeight),
                UsageTheme.MutedText,
                labelFlags);

            TextRenderer.DrawText(
                g,
                value,
                font,
                new Rectangle(Width - valueWidth, top, valueWidth, RowHeight),
                UsageTheme.PrimaryText,
                valueFlags);
        }
    }
}
