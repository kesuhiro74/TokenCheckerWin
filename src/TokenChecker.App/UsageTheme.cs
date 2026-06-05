using System.Drawing.Drawing2D;
using TokenChecker.Core;

namespace TokenChecker.App;

// Shared visual vocabulary for the popup windows (the Claude/Codex status window
// and the dedicated GitHub Copilot window) plus the bar control they share.
//
// Crucially this is the SINGLE SOURCE OF TRUTH for the 80% / 95% usage-severity
// escalation (CLAUDE.md requires the warning/critical thresholds to match
// everywhere): StatusForm.UsageAccentColor / BrandUsageColor and
// TrayIconRenderer.DetermineState all route through the thresholds defined here,
// so there is one place to change them.
internal static class UsageTheme
{
    // The single source of truth for the usage-severity escalation thresholds.
    public const double WarningPercent = 80d;
    public const double CriticalPercent = 95d;

    // Neutral light palette (mirrors the status popup's surface/cards).
    public static readonly Color Surface = Color.FromArgb(244, 246, 250);
    public static readonly Color Card = Color.FromArgb(252, 253, 255);
    public static readonly Color CardBorder = Color.FromArgb(224, 228, 235);
    public static readonly Color PrimaryText = Color.FromArgb(30, 34, 44);
    public static readonly Color SecondaryText = Color.FromArgb(70, 76, 92);
    public static readonly Color MutedText = Color.FromArgb(120, 126, 140);
    public static readonly Color TrackEmpty = Color.FromArgb(228, 232, 238);
    public static readonly Color DetailToggle = Color.FromArgb(75, 105, 195);
    public static readonly Color DetailBackground = Color.FromArgb(247, 248, 252);

    // Severity palette (0-79% / 80-94% / >=95%).
    public static readonly Color Good = Color.FromArgb(34, 165, 90);
    public static readonly Color Warning = Color.FromArgb(214, 154, 35);
    public static readonly Color Bad = Color.FromArgb(214, 70, 70);

    // GitHub Copilot brand: a deep slate that reads as GitHub and stays distinct
    // from Claude (blue) and Codex (purple).
    public static readonly Color CopilotBrand = Color.FromArgb(80, 96, 122);

    // Usage-severity color for numbers/bars: muted when there is no value, then
    // green -> amber (>=80) -> red (>=95).
    public static Color AccentColor(double? value)
    {
        if (!UsageRingRenderer.TryClampPercent(value, out var percent))
        {
            return MutedText;
        }

        return percent switch
        {
            >= CriticalPercent => Bad,
            >= WarningPercent => Warning,
            _ => Good
        };
    }

    // Like AccentColor but keeps the service brand color in the normal range so a
    // strip/row can carry the service identity while still escalating at 80/95.
    public static Color BrandUsageColor(Color brand, double? value)
    {
        if (!UsageRingRenderer.TryClampPercent(value, out var percent))
        {
            return MutedText;
        }

        return percent switch
        {
            >= CriticalPercent => Bad,
            >= WarningPercent => Warning,
            _ => brand
        };
    }

    public static Color StatusColor(ProviderStatus status)
        => status switch
        {
            ProviderStatus.Available => Good,
            ProviderStatus.NotInstalled or ProviderStatus.NotLoggedIn
                or ProviderStatus.Unauthorized or ProviderStatus.RateLimited => Warning,
            ProviderStatus.Error => Bad,
            _ => MutedText
        };

    // Blend a base color toward an accent color by `amount` (0..1).
    public static Color Tint(Color baseColor, Color accent, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(baseColor.R + (accent.R - baseColor.R) * amount),
            (int)Math.Round(baseColor.G + (accent.G - baseColor.G) * amount),
            (int)Math.Round(baseColor.B + (accent.B - baseColor.B) * amount));
    }

    // Blend a color toward white by `amount` (0..1).
    public static Color Lighten(Color color, float amount)
        => Tint(color, Color.White, amount);

    public static GraphicsPath CreateRoundedRectPath(RectangleF rect, float radius)
    {
        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var d = Math.Max(0.1f, diameter);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Frosted-glass card backdrop: a faint brand-tinted vertical gradient, a soft
    // top highlight, a brand accent pill down the left edge, and a delicate double
    // border. Brand color is decorative only — usage severity rides the numbers
    // and bars, not this background.
    public static void PaintGlassCard(Graphics g, int width, int height, Color brand)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);

        const float radius = 13f;
        var rect = new RectangleF(0, 0, width - 1, height - 1);
        using var path = CreateRoundedRectPath(rect, radius);

        var top = Lighten(Card, 0.40f);
        var bottom = Tint(Card, brand, 0.11f);
        using (var fill = new LinearGradientBrush(
            new RectangleF(0, 0, width, height), top, bottom, LinearGradientMode.Vertical))
        {
            g.FillPath(fill, path);
        }

        var prevClip = g.Clip;
        g.SetClip(path, CombineMode.Replace);

        var glossHeight = Math.Max(8f, height * 0.45f);
        using (var gloss = new LinearGradientBrush(
            new RectangleF(0, 0, width, glossHeight),
            Color.FromArgb(120, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(gloss, new RectangleF(0, 0, width, glossHeight));
        }

        var accentRect = new RectangleF(8f, 13f, 4f, height - 26f);
        using (var accentPath = CreateRoundedRectPath(accentRect, 2f))
        using (var accentBrush = new SolidBrush(brand))
        {
            g.FillPath(accentBrush, accentPath);
        }

        g.Clip = prevClip;

        var innerRect = new RectangleF(1f, 1f, width - 3f, height - 3f);
        using (var innerPath = CreateRoundedRectPath(innerRect, radius - 1f))
        using (var innerPen = new Pen(Color.FromArgb(150, 255, 255, 255)))
        {
            g.DrawPath(innerPen, innerPath);
        }

        using var border = new Pen(CardBorder);
        g.DrawPath(border, path);
    }

    // Build a title font from the requested family, falling back to Segoe UI Bold
    // if the family is not installed (so a missing display face never leaves the
    // title in an unexpected substitute face).
    public static Font CreateTitleFont(string family, float size, FontStyle style)
    {
        try
        {
            var font = new Font(family, size, style);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }
        catch
        {
            // Fall through to the default below.
        }

        return new Font("Segoe UI", 10.5F, FontStyle.Bold);
    }
}
