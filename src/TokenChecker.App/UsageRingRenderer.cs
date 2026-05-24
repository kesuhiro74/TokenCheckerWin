using System.Drawing;
using System.Drawing.Drawing2D;

namespace TokenChecker.App;

internal static class UsageRingRenderer
{
    public static bool TryClampPercent(double? value, out double percent)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            percent = 0;
            return false;
        }

        percent = Math.Clamp(value.Value, 0, 100);
        return true;
    }

    public static void Draw(
        Graphics graphics,
        Rectangle bounds,
        double? usedPercent,
        Color foreColor,
        Color emptyColor,
        Color accentColor,
        Color backColor)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(backColor);

        var size = Math.Min(bounds.Width, bounds.Height);
        var stroke = Math.Max(4f, size * 0.11f);
        var inset = stroke / 2f + 2f;
        var rect = new RectangleF(
            bounds.Left + inset,
            bounds.Top + inset,
            size - inset * 2f,
            size - inset * 2f);

        using var backgroundPen = new Pen(emptyColor, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(backgroundPen, rect, 0, 360);

        if (TryClampPercent(usedPercent, out var percent))
        {
            using var accentPen = new Pen(accentColor, stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(accentPen, rect, -90, (float)(percent / 100d * 360d));
        }

        var text = TryClampPercent(usedPercent, out var labelPercent)
            ? $"{Math.Round(labelPercent):0}%"
            : "n/a";

        using var font = new Font("Segoe UI", text == "n/a" ? 8F : 9F, FontStyle.Bold);
        using var brush = new SolidBrush(TryClampPercent(usedPercent, out _) ? foreColor : emptyColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString(text, font, brush, bounds, format);
    }
}
