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

    // Shared corner radius for the popup cards and the rounded window regions, so
    // the painted card corners and the window's clip stay in sync. Kept small to
    // match a standard Windows 11 window: at DpiUnaware + 150% display scale this
    // app-space 5px renders at ~8px physical, i.e. Win11's native window radius.
    public const float CardCornerRadius = 5f;

    // ----- Theme palettes (light / dark) -----------------------------------
    // Surface/chrome colors flip between light and dark; the severity (Good/Warning/
    // Bad) and brand colors are tuned per mode for contrast. The active palette is
    // chosen ONCE at startup (Apply, called from Program before any window is built);
    // there is NO live switching. Call sites (UsageTheme.Surface, ...) are unchanged.
    private sealed record Palette(
        Color Surface, Color Card, Color CardBorder,
        Color PrimaryText, Color SecondaryText, Color MutedText, Color SubtleText,
        Color TrackEmpty, Color DetailToggle, Color DetailBackground,
        Color Good, Color Warning, Color Bad,
        Color CopilotBrand, Color ClaudeBrand, Color CodexBrand, Color PinnedBorder,
        float GlassTopLighten, Color GlassGloss, Color GlassInner, float GlassBottomTint);

    private static readonly Palette Light = new(
        Surface: Color.FromArgb(244, 246, 250),
        Card: Color.FromArgb(252, 253, 255),
        CardBorder: Color.FromArgb(224, 228, 235),
        PrimaryText: Color.FromArgb(30, 34, 44),
        SecondaryText: Color.FromArgb(70, 76, 92),
        MutedText: Color.FromArgb(120, 126, 140),
        SubtleText: Color.FromArgb(160, 166, 180),
        TrackEmpty: Color.FromArgb(228, 232, 238),
        DetailToggle: Color.FromArgb(75, 105, 195),
        DetailBackground: Color.FromArgb(247, 248, 252),
        Good: Color.FromArgb(34, 165, 90),
        Warning: Color.FromArgb(214, 154, 35),
        Bad: Color.FromArgb(214, 70, 70),
        CopilotBrand: Color.FromArgb(80, 96, 122),
        ClaudeBrand: Color.FromArgb(74, 124, 232),
        CodexBrand: Color.FromArgb(139, 92, 214),
        PinnedBorder: Color.FromArgb(150, 80, 96, 122),
        GlassTopLighten: 0.40f,
        GlassGloss: Color.FromArgb(120, 255, 255, 255),
        GlassInner: Color.FromArgb(150, 255, 255, 255),
        GlassBottomTint: 0.11f);

    private static readonly Palette Dark = new(
        Surface: Color.FromArgb(28, 30, 36),
        Card: Color.FromArgb(38, 41, 49),
        CardBorder: Color.FromArgb(58, 62, 72),
        PrimaryText: Color.FromArgb(236, 238, 243),
        SecondaryText: Color.FromArgb(188, 193, 203),
        MutedText: Color.FromArgb(142, 148, 162),
        SubtleText: Color.FromArgb(110, 116, 130),
        TrackEmpty: Color.FromArgb(56, 60, 70),
        DetailToggle: Color.FromArgb(120, 150, 235),
        DetailBackground: Color.FromArgb(32, 35, 42),
        Good: Color.FromArgb(74, 190, 120),
        Warning: Color.FromArgb(228, 176, 72),
        Bad: Color.FromArgb(232, 96, 96),
        CopilotBrand: Color.FromArgb(140, 156, 184),
        ClaudeBrand: Color.FromArgb(108, 156, 255),
        CodexBrand: Color.FromArgb(170, 130, 238),
        PinnedBorder: Color.FromArgb(150, 140, 156, 184),
        // Dark cards are flat: no top→bottom lighten, no brand tint at the bottom,
        // and no gloss highlight, so the card reads as a single solid panel (the
        // subtle gradient/sheen is light-mode only). A faint inner outline is kept
        // purely for edge definition (it is a 1px line, not a gradient).
        GlassTopLighten: 0.0f,
        GlassGloss: Color.FromArgb(0, 255, 255, 255),
        GlassInner: Color.FromArgb(28, 255, 255, 255),
        GlassBottomTint: 0.0f);

    private static Palette _active = Light;

    // True when the dark palette is active. Resolved once at startup.
    public static bool IsDark => ReferenceEquals(_active, Dark);

    // Selects the light or dark palette. Call ONCE at startup, before any window is
    // constructed (see Program.Main). There is no live re-theming.
    public static void Apply(bool dark) => _active = dark ? Dark : Light;

    public static Color Surface => _active.Surface;
    public static Color Card => _active.Card;
    public static Color CardBorder => _active.CardBorder;
    public static Color PrimaryText => _active.PrimaryText;
    public static Color SecondaryText => _active.SecondaryText;
    public static Color MutedText => _active.MutedText;
    public static Color SubtleText => _active.SubtleText;
    public static Color TrackEmpty => _active.TrackEmpty;
    public static Color DetailToggle => _active.DetailToggle;
    public static Color DetailBackground => _active.DetailBackground;

    // Severity palette (0-79% / 80-94% / >=95%) — colors adapt per theme; the 80/95
    // THRESHOLDS (WarningPercent/CriticalPercent) are unchanged.
    public static Color Good => _active.Good;
    public static Color Warning => _active.Warning;
    public static Color Bad => _active.Bad;

    // Service brand colors (kept distinct: Claude blue / Codex purple / Copilot slate),
    // tuned per theme for contrast.
    public static Color CopilotBrand => _active.CopilotBrand;
    public static Color ClaudeBrand => _active.ClaudeBrand;
    public static Color CodexBrand => _active.CodexBrand;

    // Faint 1px outline drawn around the Copilot card when it is pinned / always-on.
    public static Color PinnedBorder => _active.PinnedBorder;

    // Usage-severity color for numbers/bars: muted when there is no value, then
    // green -> amber (>=80) -> red (>=95).
    public static Color AccentColor(double? value) => AccentColor(value, Good);

    // Like AccentColor but with a caller-chosen base color for the normal (<80%)
    // range (e.g. a user-selected Copilot accent), while still escalating to the
    // shared amber/red at the 80/95 thresholds. Muted when there is no value.
    public static Color AccentColor(double? value, Color baseColor)
    {
        if (!UsageRingRenderer.TryClampPercent(value, out var percent))
        {
            return MutedText;
        }

        return percent switch
        {
            >= CriticalPercent => Bad,
            >= WarningPercent => Warning,
            _ => baseColor
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

    // Blend a color toward black by `amount` (0..1) — preserves alpha.
    public static Color Darken(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            color.A,
            (int)Math.Round(color.R * (1f - amount)),
            (int)Math.Round(color.G * (1f - amount)),
            (int)Math.Round(color.B * (1f - amount)));
    }

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
    // and bars, not this background. Default overload uses the shared card radius
    // and paints the left pill in the brand color.
    public static void PaintGlassCard(Graphics g, int width, int height, Color brand)
        => PaintGlassCard(g, width, height, brand, CardCornerRadius, brand);

    // Overload letting the caller pick the corner radius (e.g. the Copilot window
    // rounds tighter than the status window) and the left accent pill color
    // independently of the decorative brand tint (the Copilot window colors the
    // pill with the user-selected accent while keeping a neutral slate tint).
    public static void PaintGlassCard(Graphics g, int width, int height, Color brand, float radius, Color accentPill)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Clear with the window background (Surface), NOT Card: the rounded glass is
        // painted only inside the rounded path below, so the four corner triangles
        // outside it keep this color. Surface matches the window behind an inset card,
        // so those corners blend away and the card reads as a clean rounded box (with
        // Card the corners showed as square nubs poking past the rounded border). For
        // a card that fills its window the corners are clipped by the window region,
        // so this is neutral there.
        g.Clear(Surface);

        var rect = new RectangleF(0, 0, width - 1, height - 1);
        using var path = CreateRoundedRectPath(rect, radius);

        var top = Lighten(Card, _active.GlassTopLighten);
        var bottom = Tint(Card, brand, _active.GlassBottomTint);
        using (var fill = new LinearGradientBrush(
            new RectangleF(0, 0, width, height), top, bottom, LinearGradientMode.Vertical))
        {
            g.FillPath(fill, path);
        }

        // Graphics.Clip getter hands back a fresh Region copy; dispose it so the
        // card repaint (hover polling, timer updates) does not leak one each time.
        using var prevClip = g.Clip;
        g.SetClip(path, CombineMode.Replace);

        // Top gloss: a faint light highlight. On dark it is much weaker (low alpha)
        // so it reads as a subtle sheen, not a white band.
        var gloss0 = _active.GlassGloss;
        var glossHeight = Math.Max(8f, height * 0.45f);
        using (var gloss = new LinearGradientBrush(
            new RectangleF(0, 0, width, glossHeight),
            gloss0,
            Color.FromArgb(0, gloss0.R, gloss0.G, gloss0.B),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(gloss, new RectangleF(0, 0, width, glossHeight));
        }

        var accentRect = new RectangleF(8f, 13f, 4f, height - 26f);
        using (var accentPath = CreateRoundedRectPath(accentRect, 2f))
        using (var accentBrush = new SolidBrush(accentPill))
        {
            g.FillPath(accentBrush, accentPath);
        }

        g.Clip = prevClip;

        var innerRect = new RectangleF(1f, 1f, width - 3f, height - 3f);
        using (var innerPath = CreateRoundedRectPath(innerRect, radius - 1f))
        using (var innerPen = new Pen(_active.GlassInner))
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

    // ----- GitHub Copilot card font (Moralerspace, install-conditional) -----
    // Moralerspace is NOT bundled with the app. It is used only when installed on
    // the OS; otherwise the card falls back to the standard UI face (Segoe UI) so
    // the app never depends on a font being present and never crashes. The resolved
    // family name is cached (font lookup is not free, and fonts are created per
    // paint in the value control).
    private const string CopilotFallbackFamily = "Segoe UI";

    private static readonly string[] MoralerspaceCandidates =
    {
        "Moralerspace",
        "Moralerspace Neon", "Moralerspace Argon", "Moralerspace Xenon",
        "Moralerspace Radon", "Moralerspace Krypton",
        "Moralerspace Neon HW", "Moralerspace Argon HW", "Moralerspace Xenon HW",
        "Moralerspace Radon HW", "Moralerspace Krypton HW"
    };

    private static string? _copilotFamily;
    private static bool _copilotResolved;

    // True when no Moralerspace family was found and the fallback face is in use.
    public static bool CopilotFontFellBack { get; private set; }

    // The Copilot-card font family actually in use (a Moralerspace* family if one is
    // installed, otherwise the fallback). Resolved once and cached.
    public static string CopilotFontFamily
    {
        get
        {
            if (!_copilotResolved)
            {
                _copilotResolved = true;
                _copilotFamily = CopilotFallbackFamily;
                CopilotFontFellBack = true;
                foreach (var name in MoralerspaceCandidates)
                {
                    if (FontFamilyInstalled(name))
                    {
                        _copilotFamily = name;
                        CopilotFontFellBack = false;
                        break;
                    }
                }
            }

            return _copilotFamily!;
        }
    }

    private static bool FontFamilyInstalled(string name)
    {
        try
        {
            using var family = new FontFamily(name);
            return string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // FontFamily throws if the family is not installed.
            return false;
        }
    }

    // Creates a Copilot-card font in the resolved family, re-falling back to the UI
    // face on any creation error so a font problem never takes the window down.
    public static Font CreateCopilotFont(float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font(CopilotFontFamily, size, style);
        }
        catch
        {
            // The resolved family failed to instantiate at runtime (e.g. uninstalled
            // mid-session): reflect that we are now on the fallback face.
            CopilotFontFellBack = true;
            return new Font(CopilotFallbackFamily, size, style);
        }
    }
}
