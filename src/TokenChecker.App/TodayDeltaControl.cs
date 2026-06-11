using TokenChecker.Core;

namespace TokenChecker.App;

// The promoted "since 09:00 today" delta: a spark glyph + a small "本日" label and
// a larger value, sharing a baseline. To read as a sibling of the main line, the
// label matches the "使用済み" suffix (small, muted, regular) and the value matches
// the 67% number (valueSize, bold, primary text). Only the GLYPH carries the
// daily-pace severity color (green/amber/red). Transparent background so the glass
// card gradient shows through.
internal sealed class TodayDeltaControl : Control
{
    private const int Gap = 6;       // glyph -> label
    private const int LabelGap = 5;  // label -> value
    private readonly float _valueSize;
    private readonly float _labelSize;
    private string _label = string.Empty;
    private string _value = string.Empty;
    private Color _iconColor = UsageTheme.MutedText;

    public TodayDeltaControl(float valuePointSize)
    {
        _valueSize = valuePointSize;
        // Same formula MainUsageControl uses for its "使用済み" suffix, so the "本日"
        // label is exactly the suffix size.
        _labelSize = valuePointSize * 0.46f + 1f;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public void SetContent(string label, string value, Color iconColor)
    {
        _label = label ?? string.Empty;
        _value = value ?? string.Empty;
        _iconColor = iconColor;
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
        var g = e.Graphics;

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        // Glyph sized off the value point size, drawn left + vertically centered.
        int glyphBox;
        using (var nominal = UsageTheme.CreateCopilotFont(_valueSize, FontStyle.Bold))
        {
            var glyphSize = (float)Math.Round(nominal.GetHeight(g) * 0.92f);
            var glyphTop = (Height - glyphSize) / 2f;
            TodayDeltaGlyph.Draw(g, new RectangleF(0f, glyphTop, glyphSize, glyphSize), _iconColor);
            glyphBox = (int)Math.Round(glyphSize);
        }

        var contentLeft = glyphBox + Gap;
        var avail = Math.Max(1, Width - contentLeft);

        // Label = the "使用済み" look: small, muted, regular weight.
        using var labelFont = UsageTheme.CreateCopilotFont(_labelSize, FontStyle.Regular);
        var labelWidth = string.IsNullOrEmpty(_label)
            ? 0
            : TextRenderer.MeasureText(g, _label, labelFont, new Size(int.MaxValue, int.MaxValue), flags).Width;
        var valueLeftOffset = string.IsNullOrEmpty(_label) ? 0 : labelWidth + LabelGap;

        // Value = the 67% look: valueSize, bold, primary text. Auto-shrink so a long
        // value (4-digit credits + percent, or the wider English copy) never clips.
        var size = _valueSize;
        var valueFont = UsageTheme.CreateCopilotFont(size, FontStyle.Bold);
        while (size > 9f
            && valueLeftOffset
                + TextRenderer.MeasureText(g, _value, valueFont, new Size(int.MaxValue, int.MaxValue), flags).Width > avail)
        {
            valueFont.Dispose();
            size -= 0.5f;
            valueFont = UsageTheme.CreateCopilotFont(size, FontStyle.Bold);
        }

        try
        {
            // Share a baseline so the small label and big value sit on one line.
            var valueTop = (Height - valueFont.GetHeight(g)) / 2f;
            var baseline = valueTop + TextMetrics.AscentPx(g, valueFont);

            if (!string.IsNullOrEmpty(_label))
            {
                var labelTop = baseline - TextMetrics.AscentPx(g, labelFont);
                TextRenderer.DrawText(g, _label, labelFont, new Point(contentLeft, (int)Math.Round(labelTop)), UsageTheme.MutedText, flags);
            }

            if (!string.IsNullOrEmpty(_value))
            {
                TextRenderer.DrawText(g, _value, valueFont, new Point(contentLeft + valueLeftOffset, (int)Math.Round(valueTop)), UsageTheme.PrimaryText, flags);
            }
        }
        finally
        {
            valueFont.Dispose();
        }
    }
}
