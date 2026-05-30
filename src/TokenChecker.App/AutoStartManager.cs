using Microsoft.Win32;
using System.Reflection;

namespace TokenChecker.App;

internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TokenCheckerWin";

    public static void Apply(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (runKey is null)
            {
                return;
            }

            if (enabled)
            {
                runKey.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup registration is best effort; keep the app running.
        }
    }

    private static string Quote(string value)
        => $"\"{value}\"";

    private static string BuildStartupCommand()
    {
        // Published builds (incl. single-file): ProcessPath is the real .exe, so
        // register it directly. Environment.ProcessPath is single-file safe.
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Quote(processPath);
        }

        // Development (`dotnet run`): ProcessPath is dotnet.exe, so register
        // `dotnet "<app dll>"`. Build the DLL path from AppContext.BaseDirectory
        // and the entry assembly's simple name — both single-file safe. We avoid
        // Assembly.Location, which returns "" in single-file apps (IL3000).
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(processPath)
            && !string.IsNullOrWhiteSpace(entryName))
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, entryName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return $"{Quote(processPath)} {Quote(assemblyPath)}";
            }
        }

        return Quote(Application.ExecutablePath);
    }
}
