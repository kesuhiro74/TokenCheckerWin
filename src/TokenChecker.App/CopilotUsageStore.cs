using System.Text.Json;

namespace TokenChecker.App;

internal sealed class CopilotUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public CopilotUsageStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(appData, "TokenCheckerWin", "copilot_usage.json");
    }

    // Test seam: back the store with an explicit file path so unit tests never
    // read or write the real copilot_usage.json. Schema and load/save behavior
    // are unchanged — only the location differs.
    internal CopilotUsageStore(string path) => _path = path;

    public CopilotUsageRecord? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CopilotUsageRecord>(File.ReadAllText(_path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CopilotUsageRecord record)
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(record, JsonOptions));
        }
        catch
        {
            // Persistence must never take down the tray app.
        }
    }
}
