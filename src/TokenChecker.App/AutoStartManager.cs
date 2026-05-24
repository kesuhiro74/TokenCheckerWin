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
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Quote(processPath);
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(processPath)
            && !string.IsNullOrWhiteSpace(assemblyPath)
            && File.Exists(assemblyPath))
        {
            return $"{Quote(processPath)} {Quote(assemblyPath)}";
        }

        return Quote(Application.ExecutablePath);
    }
}
