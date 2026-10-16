using System.Text.Json;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class LastUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public LastUsageStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(appData, "TokenCheckerWin", "last_usage.json");
    }

    // Test seam: lets a test drive a temp file instead of %APPDATA% (same
    // pattern as CopilotUsageStore's internal ctor).
    internal LastUsageStore(string path) => _path = path;

    public UsageSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return FromJson(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    public void Save(UsageSnapshot snapshot)
    {
        try
        {
            AtomicFile.WriteAllText(_path, ToJson(snapshot));
        }
        catch
        {
            // Persistence must never take down the tray app.
        }
    }

    internal static string ToJson(UsageSnapshot snapshot)
        => JsonSerializer.Serialize(Sanitize(snapshot), JsonOptions);

    internal static UsageSnapshot? FromJson(string json)
        => JsonSerializer.Deserialize<UsageSnapshot>(json, JsonOptions);

    // Strips the provider diagnostic Message before persisting. Everything that
    // remains is numeric usage data plus the display-only Extras (plan/model
    // display names, percentages, dates, amounts) — no tokens, emails or paths;
    // see the privacy note on ServiceExtras. Extras is carried over so a failed
    // refresh still shows the plan badge and the extra-info row from the last
    // successful fetch.
    internal static UsageSnapshot Sanitize(UsageSnapshot snapshot)
        => new(
            snapshot.CapturedAtUtc,
            snapshot.Services
                .Select(service => new ServiceUsage(
                    service.ServiceName,
                    service.Status,
                    null,
                    service.Windows,
                    service.Extras))
                .ToArray());
}
