using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace TokenChecker.App;

// Rounded "pill" usage bar shared by the status window cards/rows and the
// dedicated Copilot window. Extracted from StatusForm so both windows draw the
// exact same bar (and so the Copilot window can reuse it without duplicating the
// drawing). Colors/helpers come from UsageTheme.
internal sealed class UsageBarControl : Control
{
    private double? _value;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; } = UsageTheme.Good;

    // When true, the fill is drawn as a light→accent horizontal gradient (used by
    // the glass cards). Other modes leave it false for the original flat fill.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool UseGradient { get; set; }

    public UsageBarControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
    }

    public void SetValue(double? usedPercent)
    {
        _value = usedPercent;
        Invalidate();
    }

    // When the bar is transparent (glass cards), let the parent paint its gradient
    // slice behind us so the track sits seamlessly on the card instead of leaving
    // an opaque rectangle.
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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (BackColor != Color.Transparent)
        {
            g.Clear(BackColor);
        }

        var rect = new RectangleF(0, 0, Width, Height);
        var radius = Math.Max(2f, Height / 2f);
        using (var trackPath = UsageTheme.CreateRoundedRectPath(rect, radius))
        using (var trackBrush = new SolidBrush(UsageTheme.TrackEmpty))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (!UsageRingRenderer.TryClampPercent(_value, out var pct))
        {
            return;
        }

        var fillWidth = (float)(rect.Width * (pct / 100d));
        if (fillWidth <= 0.1f)
        {
            return;
        }

        // Always draw at least a circle-shaped pill at the start.
        var actualWidth = Math.Max(fillWidth, Height);
        actualWidth = Math.Min(actualWidth, rect.Width);
        var fillRect = new RectangleF(0, 0, actualWidth, Height);
        using var fillPath = UsageTheme.CreateRoundedRectPath(fillRect, radius);
        if (UseGradient)
        {
            using var fillBrush = new LinearGradientBrush(
                new RectangleF(0, 0, actualWidth, Height),
                UsageTheme.Lighten(AccentColor, 0.22f),
                AccentColor,
                LinearGradientMode.Horizontal);
            g.FillPath(fillBrush, fillPath);
        }
        else
        {
            using var fillBrush = new SolidBrush(AccentColor);
            g.FillPath(fillBrush, fillPath);
        }
    }
}
