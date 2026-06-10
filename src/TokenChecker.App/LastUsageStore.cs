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

    public UsageSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            var snapshot = JsonSerializer.Deserialize<UsageSnapshot>(json, JsonOptions);
            return snapshot;
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
            // Strip provider diagnostic Message before persisting; the rest is
            // numeric usage data only (no tokens, emails, or paths).
            var sanitized = new UsageSnapshot(
                snapshot.CapturedAtUtc,
                snapshot.Services
                    .Select(service => new ServiceUsage(
                        service.ServiceName,
                        service.Status,
                        null,
                        service.Windows))
                    .ToArray());

            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(sanitized, JsonOptions));
        }
        catch
        {
            // Persistence must never take down the tray app.
        }
    }
}
