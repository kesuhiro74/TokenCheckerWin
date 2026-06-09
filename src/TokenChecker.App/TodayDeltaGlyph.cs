using System.Drawing.Drawing2D;

namespace TokenChecker.App;

// Hand-drawn (GDI+) spark / burst glyph for the today-delta line — no logo / no
// font file. A four-pointed sparkle (✦): sharp rays N/E/S/W with deep concave
// notches between them, plus two tiny accent sparks for a livelier "burst". The
// whole thing is filled in the caller's color. Shared by the Copilot status-card
// today-delta line and the tray icon's today's-burn warning mark.
internal static class TodayDeltaGlyph
{
    public static void Draw(Graphics g, RectangleF dest, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var brush = new SolidBrush(color);

            var cx = dest.X + dest.Width / 2f;
            var cy = dest.Y + dest.Height / 2f;
            var outer = Math.Min(dest.Width, dest.Height) / 2f * 0.94f;
            // Small inner radius = sharp, pointed rays (a sparkle, not a star).
            var inner = outer * 0.34f;

            using (var star = new GraphicsPath())
            {
                var pts = new PointF[8];
                for (var i = 0; i < 8; i++)
                {
                    // Start at straight up (-90°), alternate outer/inner every 45°.
                    var angle = (-90 + i * 45) * (float)(Math.PI / 180d);
                    var r = (i % 2 == 0) ? outer : inner;
                    pts[i] = new PointF(cx + r * (float)Math.Cos(angle), cy + r * (float)Math.Sin(angle));
                }

                star.AddPolygon(pts);
                g.FillPath(brush, star);
            }

            // Two tiny accent sparks (upper-right, lower-left) for a "burst" feel.
            var d1 = outer * 0.26f;
            g.FillEllipse(brush, dest.X + dest.Width * 0.80f - d1 / 2f, dest.Y + dest.Height * 0.20f - d1 / 2f, d1, d1);
            var d2 = outer * 0.18f;
            g.FillEllipse(brush, dest.X + dest.Width * 0.16f - d2 / 2f, dest.Y + dest.Height * 0.82f - d2 / 2f, d2, d2);
        }
        finally
        {
            g.SmoothingMode = prevSmoothing;
        }
    }
}
