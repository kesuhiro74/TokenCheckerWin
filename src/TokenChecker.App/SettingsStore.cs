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

            // One-shot legacy migration: if the on-disk JSON has the old
            // CompactMode field but no DisplayMode field, treat CompactMode
            // as the source of truth and upgrade. Field-presence is checked
            // here (instead of in Normalize) so that switching modes at
            // runtime never re-triggers this migration.
            if (!HasDisplayModeField(json) && settings.CompactMode)
            {
                settings.DisplayMode = DisplayMode.Compact;
            }

            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static bool HasDisplayModeField(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("DisplayMode", out _);
        }
        catch
        {
            return false;
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
