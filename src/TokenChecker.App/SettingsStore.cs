using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenChecker.App;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(appData, "TokenCheckerWin", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            ApplyLegacyMigrations(json, settings);

            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    // One-shot migrations from removed legacy fields. Each is gated on the NEW
    // field being absent from the on-disk JSON (field-presence, not value) so a
    // runtime change never re-triggers a migration. The legacy fields no longer
    // exist on AppSettings, so they are read from the raw JSON here.
    // `internal` (not private) only so the unit tests can drive every migration
    // branch from a JSON string without touching the real settings.json — the
    // behavior is unchanged.
    internal static void ApplyLegacyMigrations(string json, AppSettings settings)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // CompactMode -> DisplayMode: the old compact-mode boolean predates
            // DisplayMode; treat it as the source of truth when DisplayMode is absent.
            if (!root.TryGetProperty("DisplayMode", out _) && settings.CompactMode)
            {
                settings.DisplayMode = DisplayMode.Compact;
            }

            // ShowOnStartup=false meant "start silent in the tray", so it maps BOTH
            // windows' display method to HoverPreview rather than suddenly
            // Always-showing them at startup. true/absent keeps the Always default.
            var showOnStartupFalse = root.TryGetProperty("ShowOnStartup", out var showOnStartup)
                && showOnStartup.ValueKind == JsonValueKind.False;

            if (!root.TryGetProperty("ClaudeCodexDisplayMode", out _) && showOnStartupFalse)
            {
                settings.ClaudeCodexDisplayMode = WindowDisplayMode.HoverPreview;
            }

            // CopilotWindowTrigger (Click / MouseOver / ClickThenFade — all removed),
            // OR the old ShowOnStartup=false intent, maps the Copilot window to
            // HoverPreview (even the old Click trigger, so it does not pop up at
            // startup). This also covers the case where the Copilot window is
            // migrated ON via VisibleServices / TrayIconMode="Copilot" but the old
            // settings asked to start silent — the window must not appear on launch.
            // CopilotWindowFadeSeconds is intentionally ignored: the fade-seconds
            // concept is gone (HoverPreview hides immediately on leave).
            if (!root.TryGetProperty("CopilotDisplayMode", out _)
                && (root.TryGetProperty("CopilotWindowTrigger", out _) || showOnStartupFalse))
            {
                settings.CopilotDisplayMode = WindowDisplayMode.HoverPreview;
            }

            // CopilotWindowEnabled: the earlier build toggled the Copilot window via
            // "GitHub Copilot" in VisibleServices; the old TrayIconMode=="Copilot" is
            // a supplementary signal that a Copilot consumer was intended. Either one
            // migrates the window on. Normalize() then strips "GitHub Copilot" out of
            // VisibleServices (which is Claude/Codex-only now).
            if (!root.TryGetProperty("CopilotWindowEnabled", out _))
            {
                var copilotInVisible = settings.VisibleServices is not null
                    && settings.VisibleServices.Any(service =>
                        string.Equals(service, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase));
                var trayWasCopilot = root.TryGetProperty("TrayIconMode", out var trayIconMode)
                    && trayIconMode.ValueKind == JsonValueKind.String
                    && string.Equals(trayIconMode.GetString(), "Copilot", StringComparison.OrdinalIgnoreCase);

                if (copilotInVisible || trayWasCopilot)
                {
                    settings.CopilotWindowEnabled = true;
                }
            }
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Settings persistence must never take down the tray app.
        }
    }
}
