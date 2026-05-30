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
        Color backColor,
        string? centerLabel = null)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // A fully transparent backColor means the caller already painted the
        // background (e.g. the glass card behind a transparent ring); skip the
        // clear so we don't paint over it.
        if (backColor.A != 0)
        {
            graphics.Clear(backColor);
        }

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

        // When a centerLabel is supplied (e.g. a short identifier like "C" or
        // "X"), draw it instead of the numeric percentage so the caller can
        // identify the service at a glance.
        string text;
        float fontSize;
        if (!string.IsNullOrEmpty(centerLabel))
        {
            text = centerLabel;
            // Slightly larger for a 1-2 char glyph so it stays legible.
            fontSize = Math.Max(8f, size * 0.34f);
        }
        else
        {
            text = TryClampPercent(usedPercent, out var labelPercent)
                ? $"{Math.Round(labelPercent):0}%"
                : "n/a";
            fontSize = text == "n/a" ? 8F : 9F;
        }

        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
        var hasValue = TryClampPercent(usedPercent, out _);
        var textColor = hasValue || !string.IsNullOrEmpty(centerLabel) ? foreColor : emptyColor;
        using var brush = new SolidBrush(textColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString(text, font, brush, bounds, format);
    }
}
