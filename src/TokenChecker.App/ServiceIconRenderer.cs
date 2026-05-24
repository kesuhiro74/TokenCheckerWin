using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TokenChecker.App;

internal static class ServiceIconRenderer
{
    public static Bitmap Create(string serviceName, int size = 26)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var isClaude = string.Equals(serviceName, "Claude", StringComparison.OrdinalIgnoreCase);
        var primary = isClaude ? Color.FromArgb(174, 119, 255) : Color.FromArgb(91, 173, 255);
        var secondary = isClaude ? Color.FromArgb(245, 154, 91) : Color.FromArgb(96, 214, 166);
        var glyph = isClaude ? "CC" : "CX";

        var rect = new RectangleF(1.5f, 1.5f, size - 3f, size - 3f);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var brush = new LinearGradientBrush(rect, primary, secondary, 135f);
            graphics.FillPath(brush, path);
        }

        using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.2f))
        {
            graphics.DrawEllipse(pen, rect);
        }

        using var font = new Font("Segoe UI", size * 0.34f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.FromArgb(245, 247, 252));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(glyph, font, textBrush, new RectangleF(0, 0, size, size), format);

        return bitmap;
    }
}
