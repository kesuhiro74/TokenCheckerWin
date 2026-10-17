using System.Drawing.Drawing2D;

namespace TokenChecker.App;

// The Copilot card's "this month by day" chart: a muted caption row plus a
// small bar chart with one slot per day of the current UTC month.
//
// One owner-drawn control (same reasoning and the same transparent-background
// pattern as CopilotBreakdownControl / MainUsageControl): the card paints a
// glass background, so every extra transparent child would be another node to
// keep in sync and another one CopilotWindow.AttachHandlers has to wire for
// drag/hover.
//
// Day semantics: a GitHub billing day starts at 00:00 UTC = 09:00 JST, the same
// boundary the card's "本日" delta uses, so today's bar should roughly match the
// "本日 +N" line above it.
//
// Hover shows a ToolTip ("10/7  1,826 credits") for the day under the cursor.
// ShowAlways is required because the popup is shown without activation (and in
// hover-preview mode never activates). It does not fight the window's own hover
// logic: CopilotWindow.RefreshHover only hit-tests the MAIN value rect, and the
// tray context's hover-leave check tests the whole Form.Bounds — the cursor
// stays inside the window while it is over the chart, so the popup stays open.
// Dragging still works: the card's MouseDown handler is attached to this
// control (it is not a LinkLabel/TextBoxBase) and WM_NCLBUTTONDOWN takes over
// the mouse loop.
internal sealed class CopilotDailyChartControl : Control
{
    // Caption row, then the plot area. The control's height is always the sum,
    // so the card's layout math needs no instance state.
    public const int CaptionHeight = 16;
    public const int ChartHeight = 36;
    public const int TotalHeight = CaptionHeight + ChartHeight;

    private const float FontSize = 8.5F;
    // Horizontal breathing room between neighbouring day bars.
    private const int BarGap = 2;
    // Keep the tallest bar one pixel clear of the plot ceiling.
    private const int TopInset = 1;

    private readonly ToolTip _tip = new()
    {
        AutoPopDelay = 20000,
        InitialDelay = 250,
        ReshowDelay = 80,
        ShowAlways = true
    };

    private string _caption = string.Empty;
    private IReadOnlyList<CopilotDailyBar> _bars = Array.Empty<CopilotDailyBar>();
    private decimal? _budget;
    private decimal _scale;
    private Color _accent = UsageTheme.Good;
    // Index of the day the cursor is over, so the ToolTip text is rebuilt only
    // when it actually changes.
    private int _hoverIndex = -1;

    public CopilotDailyChartControl()
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

    // The pixel height this block needs. Static so the card can lay out without
    // reading back any control state (Control.Visible reads false while the
    // popup is hidden).
    public static int HeightFor(bool show) => show ? TotalHeight : 0;

    // The bar color. Set from the card's configured accent; the bars do NOT
    // escalate at 80/95 — that belongs to the usage bar above.
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color AccentColor
    {
        get => _accent;
        set
        {
            if (_accent == value)
            {
                return;
            }

            _accent = value;
            Invalidate();
        }
    }

    // budgetCredits is the card's prorated daily budget expressed in credits
    // (null when there is no allowance), drawn as a dashed reference line.
    public void SetContent(
        string caption,
        IReadOnlyList<CopilotDailyBar> bars,
        decimal? budgetCredits,
        Color accent)
    {
        _caption = caption ?? string.Empty;
        _bars = bars ?? Array.Empty<CopilotDailyBar>();
        _budget = budgetCredits is { } budget && budget > 0m ? budget : null;
        _accent = accent;
        _scale = CopilotDailySeries.Scale(_bars, _budget);
        _hoverIndex = -1;
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
        if (_bars.Count == 0 || Width <= 0)
        {
            return;
        }

        var g = e.Graphics;
        using var font = UsageTheme.CreateCopilotFont(FontSize);
        TextRenderer.DrawText(
            g,
            _caption,
            font,
            new Rectangle(0, 0, Width, CaptionHeight),
            UsageTheme.MutedText,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

        // Flat baseline: it grounds the bars and is all an all-zero month shows.
        var baselineY = CaptionHeight + ChartHeight - 1;
        using (var track = new Pen(UsageTheme.TrackEmpty))
        {
            g.DrawLine(track, 0, baselineY, Width, baselineY);
        }

        var plotHeight = ChartHeight - 1 - TopInset;
        var slotWidth = (float)Width / _bars.Count;
        var barWidth = Math.Max(1f, slotWidth - BarGap);
        var pastColor = UsageTheme.Lighten(_accent, 0.42f);

        if (_scale > 0m)
        {
            for (var i = 0; i < _bars.Count; i++)
            {
                var bar = _bars[i];
                // Future days draw nothing (the baseline already covers them),
                // and a day with no usage row is simply 0.
                if (bar.IsFuture || bar.Credits <= 0m)
                {
                    continue;
                }

                var height = (float)(bar.Credits / _scale) * plotHeight;
                // A tiny day must still be visible.
                height = Math.Max(1f, Math.Min(plotHeight, height));

                var x = i * slotWidth + (slotWidth - barWidth) / 2f;
                using var brush = new SolidBrush(bar.IsToday ? _accent : pastColor);
                g.FillRectangle(brush, x, baselineY - height, barWidth, height);
            }

            // Dashed daily-budget reference line (the same prorated budget the
            // "本日" spark icon escalates against), only when an allowance exists.
            if (_budget is { } budget)
            {
                var y = baselineY - (float)(budget / _scale) * plotHeight;
                using var pen = new Pen(Color.FromArgb(150, UsageTheme.MutedText))
                {
                    DashStyle = DashStyle.Dash
                };
                g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }

    // Live hover text for the day under the cursor. Rebuilt only when the
    // hovered slot changes, so a moving cursor does not re-show the tip on
    // every pixel. Future days have nothing to report.
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = IndexAt(e.X, e.Y);
        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;
        if (index < 0)
        {
            _tip.Hide(this);
            return;
        }

        _tip.Show(CopilotDailySeries.TooltipText(_bars[index]), this, e.X + 12, Height + 4, 4000);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _tip.Hide(this);
    }

    // The bar slot at a client point, or -1 when there is nothing to report
    // (outside the plot area, no data, or a future day).
    private int IndexAt(int x, int y)
    {
        if (_bars.Count == 0 || Width <= 0 || y < CaptionHeight || x < 0 || x >= Width)
        {
            return -1;
        }

        var index = (int)(x / ((float)Width / _bars.Count));
        index = Math.Clamp(index, 0, _bars.Count - 1);
        return _bars[index].IsFuture ? -1 : index;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
