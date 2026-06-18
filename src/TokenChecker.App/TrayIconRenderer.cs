using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TokenChecker.Core;

namespace TokenChecker.App;

internal static class TrayIconRenderer
{
    public enum OverallState
    {
        Loading,
        Normal,
        Warning,
        Danger,
        Error,
        Unknown
    }

    private static readonly Color OuterTrack = Color.FromArgb(80, 86, 100);
    private static readonly Color InnerTrack = Color.FromArgb(64, 70, 82);

    private static readonly Color NormalOuter = Color.FromArgb(120, 170, 255);
    private static readonly Color NormalInner = Color.FromArgb(170, 140, 255);
    private static readonly Color WarningColor = Color.FromArgb(242, 191, 88);
    private static readonly Color DangerColor = Color.FromArgb(235, 95, 95);
    private static readonly Color ErrorColor = Color.FromArgb(235, 105, 105);
    private static readonly Color UnknownColor = Color.FromArgb(160, 165, 175);
    private static readonly Color LoadingColor = Color.FromArgb(190, 195, 210);

    // GitHub Copilot vertical-bar palette. Normal fill is a Copilot-leaning green
    // (clearly distinct from the Claude/Codex rings); it escalates to amber/red on
    // the same 80%/95% thresholds as everything else (UsageTheme).
    private static readonly Color CopilotNormalFill = Color.FromArgb(96, 198, 140);
    private static readonly Color CopilotBarOutline = Color.FromArgb(120, 128, 145);

    // Claude/Codex vertical-bar fills (blue / purple), echoing the former ring's
    // normal colors. Each bar still escalates to amber/red at 80/95 (UsageTheme).
    private static readonly Color ClaudeBarFill = NormalOuter;
    private static readonly Color CodexBarFill = NormalInner;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    // Materializes a drawn bitmap into a managed Icon: GetHicon() hands back an
    // unmanaged HICON that the caller must free, so clone an owned Icon from it and
    // destroy the temporary handle. Shared by every CreateXxxIcon entry point so the
    // handle-lifetime dance lives in exactly one place.
    private static Icon BitmapToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Icon CreateIcon(double? claudePercent, double? codexPercent, OverallState state, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawIcon(graphics, size, claudePercent, codexPercent, state);
        }

        return BitmapToIcon(bitmap);
    }

    // Renders the alternate tray icon for the GitHub Copilot mode: a tall vertical
    // % bar that uses as much of the icon area as possible (a rectangle), filling
    // bottom→top to `percent` with the shared 80/95 severity escalation. A null
    // percent (no plan/allowance or no data) or loading shows an empty track.
    //
    // `burnMarkColor` is the today's-burn warning: when non-null (today's burn has
    // reached the prorated daily budget band; see TrayApplicationContext) a large
    // spark is overlaid on the bar in that color (amber within 1 point below budget,
    // red over budget). Null means no mark (a calm day, well under budget).
    public static Icon CreateCopilotIcon(
        double? percent, bool loading, Color? accentBase = null, Color? burnMarkColor = null, int size = 32)
    {
        // Normal (<80%) fill: a lightened version of the configured accent so it
        // stays vivid on the small, dark tray track. Null falls back to the
        // original green. Severity (80/95) still overrides it inside the draw.
        var normalFill = accentBase is Color accent
            ? UsageTheme.Lighten(accent, 0.25f)
            : CopilotNormalFill;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawCopilotBar(graphics, size, percent, loading, normalFill, burnMarkColor);
        }

        return BitmapToIcon(bitmap);
    }

    // Renders the Claude/Codex tray icon as up to two side-by-side vertical % bars
    // (the same bar style as the Copilot icon): left = Claude (blue), right = Codex
    // (purple). Only the enabled services get a bar — one enabled service shows a
    // single wider, centered bar. Each bar fills bottom→top to its service's
    // percent and escalates to amber/red at 80/95.
    public static Icon CreateStatusBarsIcon(
        double? claudePercent, double? codexPercent, bool showClaude, bool showCodex, bool loading, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawStatusBars(graphics, size, claudePercent, codexPercent, showClaude, showCodex, loading);
        }

        return BitmapToIcon(bitmap);
    }

    private static void DrawStatusBars(
        Graphics graphics, int size, double? claudePercent, double? codexPercent, bool showClaude, bool showCodex, bool loading)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var bars = new List<(double? Percent, Color Fill)>();
        if (showClaude)
        {
            bars.Add((claudePercent, ClaudeBarFill));
        }

        if (showCodex)
        {
            bars.Add((codexPercent, CodexBarFill));
        }

        if (bars.Count == 0)
        {
            // Neither service selected — show a single empty placeholder bar so the
            // tray slot is never blank.
            bars.Add((null, ClaudeBarFill));
        }

        var topPad = size * 0.07f;
        var barHeight = size - topPad * 2f;

        // One bar reuses the Copilot bar's generous width; two bars are narrower
        // with a gap. The group is centered horizontally either way.
        var barWidth = bars.Count == 1 ? size * 0.46f : size * 0.30f;
        var gap = bars.Count == 1 ? 0f : size * 0.12f;
        var totalWidth = barWidth * bars.Count + gap * (bars.Count - 1);
        var startLeft = (size - totalWidth) / 2f;
        var radius = Math.Max(1.5f, Math.Min(barWidth * 0.22f, size * 0.12f));

        for (var i = 0; i < bars.Count; i++)
        {
            var rect = new RectangleF(startLeft + i * (barWidth + gap), topPad, barWidth, barHeight);
            DrawBar(graphics, rect, radius, size, bars[i].Percent, loading, bars[i].Fill);
        }
    }

    private static void DrawCopilotBar(
        Graphics graphics, int size, double? percent, bool loading, Color normalFill, Color? burnMarkColor)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var barWidth = size * 0.46f;
        var left = (size - barWidth) / 2f;
        var topPad = size * 0.07f;
        var barHeight = size - topPad * 2f;
        var rect = new RectangleF(left, topPad, barWidth, barHeight);
        // Sharper than a full pill: a small corner radius so the bar reads as a
        // crisp rounded rectangle. Clamped (proportional to size) so it neither
        // collapses at tiny tray sizes nor over-rounds at high DPI.
        var radius = Math.Max(1.5f, Math.Min(barWidth * 0.22f, size * 0.12f));

        DrawBar(graphics, rect, radius, size, percent, loading, normalFill);

        // today's-burn warning mark: a large spark overlaid on top of the bar,
        // filling nearly the whole icon (the bar may end up mostly hidden — that is
        // intentional, the burn alert takes over the slot on a heavy day). Only when
        // a severity color is supplied (today's burn at/over the daily budget band)
        // and never while loading.
        if (!loading && burnMarkColor is Color mark)
        {
            var inset = size * 0.04f;
            var markRect = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);
            TodayDeltaGlyph.Draw(graphics, markRect, mark);
        }
    }

    // Draws one vertical % bar (rounded track + bottom→top fill + outline) inside
    // `rect`. Shared by the Copilot icon and the Claude/Codex status bars.
    private static void DrawBar(
        Graphics graphics, RectangleF rect, float radius, int size, double? percent, bool loading, Color normalFill)
    {
        using (var trackPath = UsageTheme.CreateRoundedRectPath(rect, radius))
        using (var trackBrush = new SolidBrush(InnerTrack))
        {
            graphics.FillPath(trackBrush, trackPath);
        }

        if (!loading && UsageRingRenderer.TryClampPercent(percent, out var clamped))
        {
            var fillHeight = (float)(rect.Height * (clamped / 100d));
            if (fillHeight > 0.5f)
            {
                // Clip the fill to the rounded track so its top/bottom follow the pill.
                using var clip = UsageTheme.CreateRoundedRectPath(rect, radius);
                // Graphics.Clip getter returns a fresh Region copy each call; the
                // setter copies it back in, so dispose this one to avoid leaking a
                // Region per icon repaint (tray refreshes on every timer tick).
                using var previous = graphics.Clip;
                graphics.SetClip(clip, CombineMode.Replace);
                var fillRect = new RectangleF(rect.X, rect.Bottom - fillHeight, rect.Width, fillHeight);
                using (var fillBrush = new SolidBrush(CopilotBarColor(clamped, normalFill)))
                {
                    graphics.FillRectangle(fillBrush, fillRect);
                }

                graphics.Clip = previous;
            }
        }

        // Slightly clearer outline so the (now sharper) bar edges read crisply.
        using var outline = new Pen(CopilotBarOutline, Math.Max(1.2f, size * 0.055f));
        using var outlinePath = UsageTheme.CreateRoundedRectPath(rect, radius);
        graphics.DrawPath(outline, outlinePath);
    }

    private static Color CopilotBarColor(double percent, Color normalFill)
        => percent switch
        {
            >= UsageTheme.CriticalPercent => DangerColor,
            >= UsageTheme.WarningPercent => WarningColor,
            _ => normalFill
        };

    // Maps a today's-burn severity to the tray warning-mark color, or null when the
    // pace is Normal (well under the prorated daily budget) so no mark is drawn. The
    // severity bands (prorated daily budget) and their colors are owned by
    // CopilotWindow (GetTodayDeltaSeverity / SeverityIconColor) — the single source of
    // truth shared with the status-card today-delta line; the tray must NOT duplicate
    // those thresholds. internal for unit testing.
    internal static Color? BurnMarkColor(DeltaSeverity severity)
        => severity == DeltaSeverity.Normal ? null : CopilotWindow.SeverityIconColor(severity);

    public static OverallState DetermineState(UsageSnapshot snapshot, out double? claudePercent, out double? codexPercent)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        claudePercent = MaxPercent(claude);
        codexPercent = MaxPercent(codex);

        if (IsErrorLike(claude?.Status) && IsErrorLike(codex?.Status))
        {
            return OverallState.Error;
        }

        var highest = Math.Max(
            claudePercent ?? double.NegativeInfinity,
            codexPercent ?? double.NegativeInfinity);

        if (double.IsNegativeInfinity(highest))
        {
            return OverallState.Unknown;
        }

        return highest switch
        {
            >= UsageTheme.CriticalPercent => OverallState.Danger,
            >= UsageTheme.WarningPercent => OverallState.Warning,
            _ => OverallState.Normal
        };
    }

    private static bool IsErrorLike(ProviderStatus? status)
        => status is ProviderStatus.Error
            or ProviderStatus.NotInstalled
            or ProviderStatus.NotLoggedIn
            or ProviderStatus.Unauthorized
            or ProviderStatus.RateLimited;

    private static double? MaxPercent(ServiceUsage? service)
    {
        if (service is null || service.Status != ProviderStatus.Available)
        {
            return null;
        }

        double? max = null;
        foreach (var window in service.Windows)
        {
            if (UsageRingRenderer.TryClampPercent(window.UsedPercent, out var clamped))
            {
                max = max is null ? clamped : Math.Max(max.Value, clamped);
            }
        }

        return max;
    }

    private static void DrawIcon(Graphics graphics, int size, double? claudePercent, double? codexPercent, OverallState state)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var (outerActive, innerActive, glyphColor) = ResolveColors(state);

        var outerStroke = Math.Max(2.2f, size * 0.13f);
        var innerStroke = Math.Max(1.8f, size * 0.11f);
        var pad = outerStroke / 2f + 1.2f;
        var outerRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
        var innerInset = outerStroke + 1.2f;
        var innerRect = new RectangleF(
            outerRect.Left + innerInset,
            outerRect.Top + innerInset,
            outerRect.Width - innerInset * 2,
            outerRect.Height - innerInset * 2);

        DrawRing(graphics, outerRect, outerStroke, OuterTrack, outerActive, claudePercent, state);
        DrawRing(graphics, innerRect, innerStroke, InnerTrack, innerActive, codexPercent, state);
        DrawGlyph(graphics, size, glyphColor);
    }

    private static void DrawRing(
        Graphics graphics,
        RectangleF rect,
        float stroke,
        Color trackColor,
        Color activeColor,
        double? percent,
        OverallState state)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var trackPen = new Pen(trackColor, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(trackPen, rect, 0, 360);

        if (state == OverallState.Loading)
        {
            using var loadingPen = new Pen(activeColor, stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(loadingPen, rect, -90, 90);
            return;
        }

        if (!UsageRingRenderer.TryClampPercent(percent, out var clamped))
        {
            return;
        }

        using var activePen = new Pen(activeColor, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(activePen, rect, -90, (float)(clamped / 100d * 360d));
    }

    private static void DrawGlyph(Graphics graphics, int size, Color color)
    {
        var fontSize = Math.Max(5f, size * 0.34f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var rect = new RectangleF(0, 0, size, size);
        graphics.DrawString("T", font, brush, rect, format);
    }

    private static (Color Outer, Color Inner, Color Glyph) ResolveColors(OverallState state)
        => state switch
        {
            OverallState.Normal => (NormalOuter, NormalInner, Color.FromArgb(238, 240, 244)),
            OverallState.Warning => (WarningColor, WarningColor, Color.FromArgb(40, 30, 14)),
            OverallState.Danger => (DangerColor, DangerColor, Color.FromArgb(255, 240, 240)),
            OverallState.Error => (ErrorColor, ErrorColor, Color.FromArgb(255, 235, 235)),
            OverallState.Loading => (LoadingColor, LoadingColor, Color.FromArgb(210, 215, 225)),
            _ => (UnknownColor, UnknownColor, Color.FromArgb(220, 222, 230))
        };
}
