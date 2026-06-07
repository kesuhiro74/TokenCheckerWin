namespace TokenChecker.App;

internal enum DisplayMode
{
    Normal = 0,
    Compact = 1,
    Minimum = 2
}

// GitHub Copilot plan. The bundled monthly AI-credit allowance is NOT exposed by
// the API, so the user picks a plan (or enters a custom cap) and the App overlays
// it at display time. None = no allowance (show raw used credits only).
internal enum CopilotPlan
{
    None = 0,
    Pro = 1,
    ProPlus = 2,
    Max = 3,
    Custom = 4,
    Free = 5
}

// How a popup window is surfaced from its dedicated tray icon (per window).
internal enum WindowDisplayMode
{
    // Shown whenever the app is running while the window is enabled; re-openable
    // (tray click / menu) if the user closes it.
    Always = 0,

    // Hovering the window's tray icon fades it in; moving the mouse off the window
    // hides it immediately (with a small grace for the icon->window move). Clicking
    // the tray icon pins it (treated as always-visible until unpinned/closed).
    HoverPreview = 1
}

// Accent color for the GitHub Copilot card's numbers + bar (and the tray %-bar):
// it sets the normal (<80%) "good" color, while the shared 80/95 severity
// escalation (amber/red) still applies. Green is the default (the original look).
// Values are serialized by NAME (JsonStringEnumConverter), so older settings.json
// that stored "Blue"/"Sky"/"Slate" still parse correctly after this reordering.
internal enum CopilotAccent
{
    Green = 0,
    Blue = 1,
    Sky = 2,
    Purple = 3,
    Slate = 4
}

// App color theme for the windows. System follows the Windows app color mode
// (light/dark) at startup; Light/Dark force a mode. Applied at startup only (a
// change takes effect on the next launch).
internal enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

// UI language. Applied at startup only (a change needs an app restart), like Theme.
internal enum AppLanguage
{
    System = 0,
    English = 1,
    Japanese = 2
}

internal sealed class AppSettings
{
    // GitHub Copilot is keyed in snapshots by this exact provider name. (It is no
    // longer placed in VisibleServices — CopilotWindowEnabled is its on/off.)
    public const string CopilotServiceName = "GitHub Copilot";

    // Bundled monthly AI credits per plan. Flex allocation can change in the
    // market, so this is the ONE place to edit the plan values.
    // Copilot Free bundles a small monthly AI-credit allowance (code completions and
    // chat have their own separate quotas that the personal billing token cannot read,
    // so only this credit meter is shown for Free).
    public const int FreeCredits = 200;
    public const int ProCredits = 1500;
    public const int ProPlusCredits = 7000;
    public const int MaxCredits = 20000;

    public static readonly int[] AllowedRefreshIntervalSeconds = [30, 60, 300, 600];

    // ----- Common ----------------------------------------------------------

    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool AutoStartEnabled { get; set; }

    // App color theme. Applied at startup only (a change needs an app restart).
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    // UI language. Applied at startup only (a change needs an app restart).
    public AppLanguage Language { get; set; } = AppLanguage.System;

    // Kept for backward compatibility with settings.json written by older builds
    // that only knew about a compact-mode boolean. Normalize() reconciles this
    // with the DisplayMode field (the source of truth).
    public bool CompactMode { get; set; }

    // ----- Claude / Codex window -------------------------------------------

    // The status window content layout (independent of the Always/HoverPreview
    // display method below).
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Normal;

    // Which Claude/Codex cards are shown inside the status window.
    public string[] VisibleServices { get; set; } = ["Claude", "Codex"];

    public FormLocation? StatusFormLocation { get; set; }

    public bool ClaudeCodexWindowEnabled { get; set; } = true;

    public WindowDisplayMode ClaudeCodexDisplayMode { get; set; } = WindowDisplayMode.Always;

    // ----- GitHub Copilot window (opt-in; default off) ----------------------

    public bool CopilotWindowEnabled { get; set; }

    public WindowDisplayMode CopilotDisplayMode { get; set; } = WindowDisplayMode.Always;

    public CopilotPlan CopilotPlan { get; set; } = CopilotPlan.None;

    // Custom monthly allowance when CopilotPlan == Custom. 0/negative = unset.
    public int CopilotCustomCredits { get; set; }

    public FormLocation? CopilotWindowLocation { get; set; }

    // Default accent for the Copilot window (numbers stay near-black; this colors
    // the bar, the left divider pill, and the pinned outline). Blue by default; an
    // explicitly-saved older value (e.g. "Green") is preserved.
    public CopilotAccent CopilotAccent { get; set; } = CopilotAccent.Blue;

    public bool IsServiceVisible(string serviceName)
        => VisibleServices?.Any(service => string.Equals(service, serviceName, StringComparison.OrdinalIgnoreCase)) == true;

    // Provider gates: fetch ONLY what an enabled window/service needs, so a
    // disabled window never triggers its provider (no Claude/Codex calls when the
    // status window is off; no billing endpoint when the Copilot window is off).
    public bool ClaudeProviderEnabled => ClaudeCodexWindowEnabled && IsServiceVisible("Claude");

    public bool CodexProviderEnabled => ClaudeCodexWindowEnabled && IsServiceVisible("Codex");

    public bool CopilotProviderEnabled => CopilotWindowEnabled;

    // Resolves the plan/custom selection to a monthly credit allowance, or null
    // when no allowance applies. The App overlays this on the API's raw Used.
    public int? CopilotCreditAllowance()
        => CopilotPlan switch
        {
            CopilotPlan.Free => FreeCredits,
            CopilotPlan.Pro => ProCredits,
            CopilotPlan.ProPlus => ProPlusCredits,
            CopilotPlan.Max => MaxCredits,
            CopilotPlan.Custom => CopilotCustomCredits > 0 ? CopilotCustomCredits : null,
            _ => null
        };

    // Title shown on the Copilot card. The full service name ("GitHub Copilot")
    // lives in the settings dialog and tray tooltip instead.
    public string CopilotPlanTitle()
        => CopilotPlan switch
        {
            CopilotPlan.Free => "Copilot Free",
            CopilotPlan.Pro => "Copilot Pro",
            CopilotPlan.ProPlus => "Copilot Pro+",
            CopilotPlan.Max => "Copilot Max",
            CopilotPlan.Custom => CopilotCustomCredits > 0
                ? $"Custom {CopilotCustomCredits:N0} credits"
                : "Custom",
            _ => "GitHub Copilot"
        };

    // Resolves the selected accent to the base color for the Copilot numbers + bar
    // (and tray %-bar) in the normal (<80%) range. Severity still escalates to
    // amber/red at 80/95 via UsageTheme; this only sets the "good" color. Green maps
    // to UsageTheme.Good (the original look).
    public Color CopilotAccentColor()
        => CopilotAccent switch
        {
            CopilotAccent.Blue => Color.FromArgb(52, 110, 210),
            CopilotAccent.Sky => Color.FromArgb(40, 150, 214),
            CopilotAccent.Purple => Color.FromArgb(139, 92, 214),
            CopilotAccent.Slate => UsageTheme.CopilotBrand,
            _ => UsageTheme.Good
        };

    public void Normalize()
    {
        if (!AllowedRefreshIntervalSeconds.Contains(RefreshIntervalSeconds))
        {
            RefreshIntervalSeconds = 60;
        }

        VisibleServices ??= [];

        // VisibleServices is Claude/Codex card selection only; the Copilot window
        // is gated by CopilotWindowEnabled, never by this list.
        VisibleServices = VisibleServices
            .Where(service => string.Equals(service, "Claude", StringComparison.OrdinalIgnoreCase)
                || string.Equals(service, "Codex", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!Enum.IsDefined(DisplayMode))
        {
            DisplayMode = DisplayMode.Normal;
        }

        if (!Enum.IsDefined(ClaudeCodexDisplayMode))
        {
            ClaudeCodexDisplayMode = WindowDisplayMode.Always;
        }

        if (!Enum.IsDefined(CopilotDisplayMode))
        {
            CopilotDisplayMode = WindowDisplayMode.Always;
        }

        if (!Enum.IsDefined(CopilotPlan))
        {
            CopilotPlan = CopilotPlan.None;
        }

        if (!Enum.IsDefined(CopilotAccent))
        {
            CopilotAccent = CopilotAccent.Blue;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemeMode.System;
        }

        if (!Enum.IsDefined(Language))
        {
            Language = AppLanguage.System;
        }

        CopilotCustomCredits = Math.Max(0, CopilotCustomCredits);

        // CompactMode is a derived back-compat write only (see SettingsStore.Load
        // for the one-shot CompactMode -> DisplayMode migration).
        CompactMode = DisplayMode == DisplayMode.Compact;
    }

    public AppSettings Clone()
        => new()
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            AutoStartEnabled = AutoStartEnabled,
            Theme = Theme,
            Language = Language,
            CompactMode = CompactMode,
            DisplayMode = DisplayMode,
            VisibleServices = VisibleServices.ToArray(),
            StatusFormLocation = StatusFormLocation,
            ClaudeCodexWindowEnabled = ClaudeCodexWindowEnabled,
            ClaudeCodexDisplayMode = ClaudeCodexDisplayMode,
            CopilotWindowEnabled = CopilotWindowEnabled,
            CopilotDisplayMode = CopilotDisplayMode,
            CopilotPlan = CopilotPlan,
            CopilotCustomCredits = CopilotCustomCredits,
            CopilotWindowLocation = CopilotWindowLocation,
            CopilotAccent = CopilotAccent
        };
}

internal readonly record struct FormLocation(int X, int Y)
{
    public Point ToPoint() => new(X, Y);

    public static FormLocation FromPoint(Point point) => new(point.X, point.Y);
}
