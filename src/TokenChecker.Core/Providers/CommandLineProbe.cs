namespace TokenChecker.Core.Providers;

// Locates a CLI on PATH (honoring PATHEXT on Windows). Public so the App can reuse
// the exact same probe instead of carrying a duplicate copy; Core has no
// InternalsVisibleTo, so cross-assembly helpers are public (same convention as
// ClaudeUsageStatusMapper / CodexAccountClassifier). No secrets are involved — this
// only reads the PATH/PATHEXT environment and tests File.Exists.
public static class CommandLineProbe
{
    public static bool TryFindOnPath(string commandName, out string commandPath)
    {
        commandPath = string.Empty;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? GetPathExtensions()
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, commandName + extension);
                if (File.Exists(candidate))
                {
                    commandPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] GetPathExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return new[] { ".exe", ".cmd", ".bat", string.Empty };
        }

        return pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
