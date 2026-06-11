using TokenChecker.Core;

namespace TokenChecker.App;

// Renders a large value plus a smaller, fixed-grey suffix on a shared baseline,
// inside a fixed box — so the hover value-swap repaints text only and never
// changes layout (no jitter). e.g. "66%" (15pt bold, near-black) + "使用済み"
// (~7.9pt, muted grey). The number color is fixed (not the accent); only the
// card bar carries the configurable accent.
internal sealed class MainUsageControl : Control
{
    private const int Gap = 6;
    private readonly float _valueSize;
    private readonly float _suffixSize;
    private string _value = "—";
    private string _suffix = string.Empty;
    private Color _valueColor = UsageTheme.MutedText;

    public MainUsageControl(float valuePointSize)
    {
        _valueSize = valuePointSize;
        // One point larger than before, but still clearly smaller than the value.
        _suffixSize = valuePointSize * 0.46f + 1f;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public void SetContent(string value, string suffix, Color valueColor)
    {
        _value = value ?? string.Empty;
        _suffix = suffix ?? string.Empty;
        _valueColor = valueColor;
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
        using var valueFont = UsageTheme.CreateCopilotFont(_valueSize, FontStyle.Bold);
        using var suffixFont = UsageTheme.CreateCopilotFont(_suffixSize, FontStyle.Regular);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        // Vertically center the big value; the small suffix shares its baseline.
        var valueTop = (Height - valueFont.GetHeight(g)) / 2f;
        var baseline = valueTop + TextMetrics.AscentPx(g, valueFont);

        TextRenderer.DrawText(g, _value, valueFont, new Point(0, (int)Math.Round(valueTop)), _valueColor, flags);

        if (!string.IsNullOrEmpty(_suffix))
        {
            var valueWidth = TextRenderer.MeasureText(g, _value, valueFont, new Size(int.MaxValue, int.MaxValue), flags).Width;
            var suffixTop = baseline - TextMetrics.AscentPx(g, suffixFont);
            // Fixed muted grey — a few steps lighter than the near-black number,
            // and not affected by the accent/color setting.
            TextRenderer.DrawText(g, _suffix, suffixFont, new Point(valueWidth + Gap, (int)Math.Round(suffixTop)), UsageTheme.MutedText, flags);
        }
    }
}
